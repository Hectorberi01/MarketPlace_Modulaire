using MediatR;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Cart.Application.Carts.Queries;
using Marketplace.Modules.Identity.Application.Addresses;
using Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder;
using Marketplace.Modules.Payments.Application.Payments.Commands;
using Marketplace.Modules.Payments.Application.Payments.Commands.InitiatePayment;
using Marketplace.Modules.Payments.Application.Payments.Queries;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>
/// BFF Mobile — checkout & paiement. /checkout/pay orchestre la création de la
/// commande (depuis le panier actif) puis l'initiation du paiement en un appel.
/// </summary>
public static class MobileCheckoutEndpoints
{
    public static IEndpointRouteBuilder MapMobileCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        var checkout = app.MapGroup("/mobile/checkout").WithTags("Mobile · Checkout").RequireAuthorization();

        checkout.MapPost("/pay", PayAsync);

        // Options de livraison (flat-rate, sans état) + devis final (sous-total + livraison).
        checkout.MapGet("/shipping-options", ShippingOptions);
        checkout.MapPost("/shipping-options", ShippingOptions);
        checkout.MapGet("/quote", QuoteAsync);

        // Carnet d'adresses (scopé à l'acheteur du jeton).
        checkout.MapGet("/addresses", ListAddressesAsync);
        checkout.MapPost("/addresses", AddAddressAsync);
        checkout.MapPut("/addresses/{id:guid}", UpdateAddressAsync);
        checkout.MapDelete("/addresses/{id:guid}", DeleteAddressAsync);
        checkout.MapPut("/addresses/{id:guid}/default", SetDefaultAddressAsync);

        // Polling du statut de paiement (anonyme : appelé en boucle par le client).
        app.MapGet("/mobile/payments/{id:guid}/status", PaymentStatusAsync)
            .WithTags("Mobile · Checkout").RequireAuthorization();

        // Retour de page hébergée (FedaPay/carte) : réconcilie le statut auprès du PSP.
        app.MapPost("/mobile/payments/{id:guid}/confirm", ConfirmRedirectAsync)
            .WithTags("Mobile · Checkout").RequireAuthorization();

        return app;
    }

    /// <summary>
    /// Confirme un paiement au retour d'une page hébergée (interroge le PSP).
    /// Réservé au propriétaire du paiement.
    ///
    /// Le contrôle d'appartenance manquait ici alors que `PaymentStatusAsync`, juste
    /// en dessous, le fait correctement — cette route n'avait même pas de
    /// `ClaimsPrincipal`. On pouvait donc énumérer les identifiants de paiement et
    /// déclencher des appels FedaPay pour le compte d'autrui. L'issue restait dictée
    /// par le prestataire, ce qui bornait les dégâts, mais le trou était réel.
    /// </summary>
    private static async Task<IResult> ConfirmRedirectAsync(
        ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var payment = await sender.Send(new GetPaymentQuery(id), ct);
        if (payment.IsFailure || payment.Value.BuyerId != buyerId)
        {
            // 404 comme le polling : ne pas confirmer l'existence du paiement.
            return Results.NotFound();
        }

        return (await sender.Send(new ConfirmPaymentFromRedirectCommand(id), ct))
            .Match(() => Results.Ok(new { paymentId = id }));
    }

    /// <summary>
    /// Crée la commande depuis le panier actif puis initie le paiement (1 appel).
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// IDEMPOTENCE — l'en-tête `Idempotency-Key` évite le double paiement.
    ///
    /// Le client dispose de 20 secondes de délai de réception. Sur un réseau mobile
    /// béninois, le serveur peut parfaitement créer la commande et initier le
    /// paiement pendant que l'application abandonne : l'acheteur voit « Délai
    /// dépassé », réappuie, et paie deux fois.
    ///
    /// Rejouer la requête ne suffirait pas à s'en protéger : `PlaceOrderCommand`
    /// VIDE LE PANIER. La seconde tentative échouerait sur « panier vide » — l'
    /// acheteur croirait son achat perdu alors qu'il est passé. On mémorise donc la
    /// RÉPONSE et on la resert telle quelle.
    ///
    /// Stockage dans le cache distribué (Redis en production, mémoire en
    /// développement) plutôt qu'en base : la fenêtre utile est de quelques minutes,
    /// une table et sa migration seraient disproportionnées, et Redis est déjà une
    /// dépendance obligatoire de la pile. Rétention de 24 h.
    ///
    /// ⚠️ Ce n'est PAS un verrou : deux requêtes rigoureusement simultanées peuvent
    /// encore passer toutes les deux. Le cas visé est le réessai après échec, qui est
    /// celui qu'on observe. Fermer la fenêtre demanderait un SETNX atomique, donc un
    /// accès direct au multiplexeur Redis.
    ///
    /// L'en-tête est FACULTATIF : les versions de l'application déjà installées ne
    /// l'envoient pas, et doivent continuer de fonctionner.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> PayAsync(
        ClaimsPrincipal user, PayRequest request, HttpContext http,
        ISender sender, IDistributedCache cache, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        string? idempotencyCacheKey = null;
        if (http.Request.Headers.TryGetValue("Idempotency-Key", out var rawKey)
            && !string.IsNullOrWhiteSpace(rawKey))
        {
            // La clé est HACHÉE, et l'acheteur entre dans le hachage : une clé
            // arbitraire venant du client ne doit ni polluer l'espace de noms du
            // cache, ni permettre de lire la réponse de quelqu'un d'autre.
            idempotencyCacheKey = IdempotencyCacheKey(buyerId, rawKey.ToString());

            var replay = await cache.GetStringAsync(idempotencyCacheKey, ct);
            if (!string.IsNullOrEmpty(replay))
            {
                // Réponse identique au premier appel : le client ne voit aucune
                // différence entre « ça a marché » et « ça avait déjà marché ».
                return Results.Content(replay, "application/json");
            }
        }

        // Adresse de livraison choisie : on la résout côté serveur (jamais depuis
        // le client) puis on la fige sur la commande.
        ShippingAddressInput? shipTo = null;
        if (request.AddressId is { } addressId)
        {
            var addresses = await sender.Send(new ListAddressesQuery(buyerId), ct);
            if (addresses.IsSuccess)
            {
                var a = addresses.Value.FirstOrDefault(x => x.Id == addressId);
                if (a is not null)
                {
                    shipTo = new ShippingAddressInput(a.Label, a.Recipient, a.Line1, a.Line2, a.City, a.Country, a.Phone);
                }
            }
        }

        // Forfait de livraison choisi : on résout son montant côté serveur (jamais
        // depuis le client) pour le figer sur la commande et alimenter le solde
        // « frais de livraison » de la plateforme.
        var shippingCode = (request.ShippingCode ?? "standard").ToLowerInvariant();
        var rate = Array.Find(_rates, r => r.Code == shippingCode);
        var shippingFee = rate.Code is null ? _rates[0].Amount : rate.Amount;

        var placed = await sender.Send(new PlaceOrderCommand(buyerId, shipTo, shippingFee), ct);
        if (placed.IsFailure)
        {
            return placed.Match(_ => Results.Ok()); // mappe l'erreur (panier vide, stock…)
        }

        var orderId = placed.Value;
        var payment = await sender.Send(
            new InitiatePaymentCommand(
                orderId, request.Method, request.Provider,
                request.Flow ?? "HostedCheckout", request.ReturnUrl, request.CancelUrl, request.PayerPhone), ct);

        if (payment.IsFailure)
        {
            // Échec d'initiation : on ne mémorise RIEN. La commande existe mais
            // aucun paiement n'a démarré ; l'acheteur doit pouvoir réessayer, et
            // resservir cet échec le bloquerait pendant 24 heures.
            return payment.Match(result => Results.Ok(new { orderId, payment = result }));
        }

        var body = JsonSerializer.Serialize(new { orderId, payment = payment.Value }, WebJson);

        if (idempotencyCacheKey is not null)
        {
            await cache.SetStringAsync(
                idempotencyCacheKey,
                body,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) },
                ct);
        }

        return Results.Content(body, "application/json");
    }

    /// <summary>
    /// Options de sérialisation IDENTIQUES à celles de `Results.Ok`.
    ///
    /// ⚠️ Indispensable, et facile à manquer. `Results.Ok` sérialise avec les
    /// conventions web d'ASP.NET — noms de propriétés en camelCase. Un
    /// `JsonSerializer.Serialize` nu, lui, conserve le PascalCase des records du
    /// domaine. La réponse rejouée depuis le cache aurait donc porté des noms de
    /// champs différents de la réponse d'origine, et l'application mobile n'y aurait
    /// plus rien reconnu — un bug qui ne se serait manifesté qu'au premier réessai
    /// réel, c'est-à-dire chez un acheteur, sur un réseau dégradé.
    /// </summary>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Clé de cache d'idempotence : liée à l'ACHETEUR et hachée.
    ///
    /// Le hachage sert deux buts. Il neutralise une clé cliente exotique — longueur
    /// démesurée, séparateurs, caractères de contrôle — qui pourrait sinon écraser
    /// une autre entrée du cache. Et l'inclusion de l'identifiant acheteur empêche
    /// de rejouer la réponse d'autrui en devinant sa clé.
    /// </summary>
    private static string IdempotencyCacheKey(Guid buyerId, string rawKey)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{buyerId:N}:{rawKey}"));
        return $"checkout:idem:{Convert.ToHexString(digest)}";
    }

    /// <summary>Statut courant du paiement (polling). Réservé au propriétaire du paiement.</summary>
    private static async Task<IResult> PaymentStatusAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }
        var result = await sender.Send(new GetPaymentQuery(id), ct);
        // 404 si absent OU non possédé (anti-IDOR, pas de divulgation).
        return result.IsSuccess && result.Value.BuyerId == buyerId
            ? Results.Ok(result.Value)
            : Results.NotFound();
    }

    // Tarifs de livraison (forfaits). À terme : calcul géolocalisé via le module Shipping.
    private static readonly (string Code, string Label, string Eta, decimal Amount)[] _rates =
    {
        ("standard", "Standard", "Livraison sous 24h à 48h", 1500m),
        ("express", "Express", "Livraison en moins de 3h", 3000m),
    };

    /// <summary>Options de livraison disponibles (forfaits).</summary>
    private static IResult ShippingOptions()
        => Results.Ok(_rates.Select(r => new { code = r.Code, label = r.Label, eta = r.Eta, amount = r.Amount, currency = "XOF" }));

    /// <summary>Devis : sous-total du panier + livraison choisie = total à payer.</summary>
    private static async Task<IResult> QuoteAsync(ClaimsPrincipal user, string? shipping, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var cartResult = await sender.Send(new GetActiveCartQuery(buyerId), ct);
        if (cartResult.IsFailure)
        {
            return Results.BadRequest(new { error = "cart_unavailable" });
        }

        var cart = cartResult.Value;
        var code = (shipping ?? "standard").ToLowerInvariant();
        var rate = Array.Find(_rates, r => r.Code == code);
        if (rate.Code is null)
        {
            rate = _rates[0];
        }

        return Results.Ok(new
        {
            subtotal = cart.Subtotal,
            discount = cart.TotalSellerDiscount + cart.TotalPlatformDiscount,
            shipping = new { code = rate.Code, label = rate.Label, eta = rate.Eta, amount = rate.Amount },
            shippingOptions = _rates.Select(r => new { code = r.Code, label = r.Label, eta = r.Eta, amount = r.Amount }),
            total = cart.GrandTotal + rate.Amount,
            currency = cart.Currency,
        });
    }

    /// <summary>Liste les adresses de l'acheteur (défaut en tête).</summary>
    private static async Task<IResult> ListAddressesAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListAddressesQuery(userId), ct)).Match(r => Results.Ok(r));

    /// <summary>Ajoute une adresse au carnet.</summary>
    private static async Task<IResult> AddAddressAsync(ClaimsPrincipal user, AddressRequest request, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new AddAddressCommand(
            userId, request.Label, request.Recipient, request.Line1, request.Line2,
            request.City, request.Country, request.Phone, request.MakeDefault), ct);
        return result.Match(id => Results.Ok(new { id }));
    }

    /// <summary>Met à jour une adresse du carnet (scopée à l'acheteur du jeton).</summary>
    private static async Task<IResult> UpdateAddressAsync(ClaimsPrincipal user, Guid id, AddressRequest request, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new UpdateAddressCommand(
            userId, id, request.Label, request.Recipient, request.Line1, request.Line2,
            request.City, request.Country, request.Phone, request.MakeDefault), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Supprime une adresse du carnet.</summary>
    private static async Task<IResult> DeleteAddressAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new DeleteAddressCommand(userId, id), ct)).Match(() => Results.NoContent());

    /// <summary>Définit l'adresse par défaut.</summary>
    private static async Task<IResult> SetDefaultAddressAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new SetDefaultAddressCommand(userId, id), ct)).Match(() => Results.NoContent());

    public sealed record PayRequest(
        string Method, string Provider, string? Flow, string? ReturnUrl, string? CancelUrl, string? PayerPhone,
        Guid? AddressId = null, string? ShippingCode = null);

    public sealed record AddressRequest(
        string? Label, string? Recipient, string Line1, string? Line2,
        string City, string? Country, string? Phone, bool MakeDefault);
}
