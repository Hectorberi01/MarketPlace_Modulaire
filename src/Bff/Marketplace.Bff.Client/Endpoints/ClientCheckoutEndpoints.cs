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
using Marketplace.Modules.Ordering.Application.Orders.Queries;
using Marketplace.Modules.Payments.Application.Payments.Commands;
using Marketplace.Modules.Payments.Application.Payments.Commands.InitiatePayment;
using Marketplace.Modules.Payments.Application.Payments.Queries;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Shipping.Contracts;

namespace Marketplace.Bff.Client.Endpoints;

/// <summary>
/// BFF Client — checkout & paiement. /checkout/pay orchestre la création de la
/// commande (depuis le panier actif) puis l'initiation du paiement en un appel.
/// </summary>
public static class ClientCheckoutEndpoints
{
    public static IEndpointRouteBuilder MapClientCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        var checkout = app.MapGroup("/client/checkout").WithTags("Client · Checkout").RequireAuthorization();

        checkout.MapPost("/pay", PayAsync);

        // Options de livraison, tarifées par ZONE DE DESTINATION (`?commune=`), et devis
        // final (sous-total + livraison). Sans commune, c'est le tarif de repli qui
        // répond — l'acheteur consulte souvent avant d'avoir choisi son adresse.
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
        app.MapGet("/client/payments/{id:guid}/status", PaymentStatusAsync)
            .WithTags("Client · Checkout").RequireAuthorization();

        // Retour de page hébergée (FedaPay/carte) : réconcilie le statut auprès du PSP.
        app.MapPost("/client/payments/{id:guid}/confirm", ConfirmRedirectAsync)
            .WithTags("Client · Checkout").RequireAuthorization();

        // ─────────────────────────────────────────────────────────────────────────────
        // REPRENDRE LE PAIEMENT D'UNE COMMANDE DÉJÀ PASSÉE.
        //
        // `/checkout/pay` crée une commande DEPUIS LE PANIER puis la paie. Quand le
        // paiement échoue, la commande existe et le panier a été vidé : cette route
        // n'est donc plus utilisable, et l'acheteur se retrouvait devant une commande
        // impayable, sans aucun bouton pour recommencer. Le seul recours était le
        // support.
        //
        // Ici, la commande existe déjà : ni panier, ni adresse, ni frais de livraison à
        // recalculer. Tout est figé sur la commande — c'est justement ce qui rend la
        // reprise sûre, le montant ne pouvant plus bouger entre deux tentatives.
        // ─────────────────────────────────────────────────────────────────────────────
        app.MapPost("/client/orders/{id:guid}/pay", PayExistingOrderAsync)
            .WithTags("Client · Checkout").RequireAuthorization();

        return app;
    }

    /// <summary>
    /// Initie une NOUVELLE tentative de paiement sur une commande existante.
    ///
    /// Aucune clé d'idempotence ici, contrairement à <c>/checkout/pay</c> : là-bas elle
    /// protège d'un double appui qui créerait deux commandes. Ici la commande est déjà
    /// créée, rien ne peut être dupliqué, et c'est le domaine qui interdit deux paiements
    /// concurrents — après avoir vérifié auprès du prestataire, pas sur la foi d'un
    /// statut local peut-être périmé.
    /// </summary>
    private static async Task<IResult> PayExistingOrderAsync(
        ClaimsPrincipal user, Guid id, PayOrderRequest request, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        // Propriété vérifiée AVANT toute action : sans ce contrôle, n'importe quel
        // porteur de jeton pourrait déclencher des appels PSP sur la commande d'autrui.
        // 404 et non 403, pour ne pas confirmer l'existence de la commande.
        var order = await sender.Send(new GetOrderQuery(id), ct);
        if (order.IsFailure || order.Value.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var payment = await sender.Send(
            new InitiatePaymentCommand(
                id, request.Method, request.Provider,
                request.Flow ?? "HostedCheckout", request.ReturnUrl, request.CancelUrl, request.PayerPhone), ct);

        return payment.Match(result => Results.Ok(new { orderId = id, payment = result }));
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
        if (ClientBff.CurrentUserId(user) is not { } buyerId)
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
    /// Ce n'est PAS un verrou : deux requêtes rigoureusement simultanées peuvent
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
        ISender sender, IDistributedCache cache, IShippingModuleApi shipping, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } buyerId)
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
                if (a is null)
                {
                    return Results.BadRequest(new { error = "Adresse de livraison introuvable." });
                }

                // ─────────────────────────────────────────────────────────────────────
                // ON REFUSE DE PAYER UNE LIVRAISON QU'ON NE SAIT PAS FAIRE.
                //
                // Les adresses saisies avant la refonte n'ont ni commune normalisée, ni
                // point de repère, ni forcément un téléphone joignable. Au Bénin, un
                // coursier sans repère et sans numéro à appeler ne trouve pas la maison :
                // le colis revient, l'acheteur est remboursé, et tout le monde a perdu son
                // temps et ses frais.
                //
                // Mieux vaut un refus explicite AVANT le paiement qu'un échec de livraison
                // après. L'application renvoie l'acheteur vers l'écran d'adresse, où une
                // simple modification suffit à la compléter.
                // ─────────────────────────────────────────────────────────────────────
                if (!a.IsComplete)
                {
                    return Results.BadRequest(new
                    {
                        error = "Cette adresse est incomplète. Ajoutez la commune, un point de repère et un numéro de téléphone avant de commander.",
                        code = "address_incomplete",
                        addressId = a.Id,
                    });
                }

                shipTo = new ShippingAddressInput(
                    a.Label, a.Recipient, a.Phone,
                    a.CommuneCode, a.Quartier, a.Landmark, a.Line1, a.CountryCode,
                    a.Latitude, a.Longitude);
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // LE TARIF VIENT DE LA GRILLE, ET DE LA COMMUNE RÉELLEMENT RETENUE.
        //
        // Le montant est résolu côté serveur — jamais depuis le client — et il l'est
        // pour `shipTo.CommuneCode`, c'est-à-dire l'adresse effectivement figée sur la
        // commande, pas celle que l'écran croyait afficher. Si l'acheteur a changé
        // d'adresse entre le devis et le paiement, le prix change ici : c'est
        // `expectedTotal` qui refusera l'écart, proprement, au lieu de le débiter en
        // silence.
        //
        // Un code de service inconnu retombe sur la PREMIÈRE option de la zone, triée
        // par montant croissant — donc la moins chère. Choisir la plus chère par
        // défaut ferait payer un client pour une faute de frappe de son application.
        // ═════════════════════════════════════════════════════════════════════════
        var shippingCode = (request.ShippingCode ?? "standard").ToLowerInvariant();
        var options = await shipping.GetRatesForCommuneAsync(shipTo?.CommuneCode, ct);
        var rate = options.FirstOrDefault(r => r.ServiceCode == shippingCode)
                   ?? options.FirstOrDefault();

        // Le module s'engage à ne jamais renvoyer de liste vide — les forfaits de
        // secours répondent toujours. Si l'engagement est rompu, on REFUSE la
        // commande : facturer zéro de livraison serait offrir le transport à toute la
        // place sans que rien ne le signale, et prendre un montant arbitraire serait
        // pire encore.
        if (rate is null)
        {
            return Results.Problem(
                title: "checkout.shipping_unavailable",
                detail: "Aucun tarif de livraison n'a pu être déterminé pour cette destination.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var shippingFee = rate.Amount;

        // ═════════════════════════════════════════════════════════════════════════
        // LE TOTAL ATTENDU EST OBLIGATOIRE SUR CETTE SURFACE, ET LE REFUS EST EXPLICITE.
        //
        // C'est la seule surface où un être humain lit un prix avant d'appuyer sur un
        // bouton ; c'est donc la seule qui puisse s'engager dessus. Le champ est
        // `nullable` dans le contrat pour une raison précise : s'il était un `decimal`
        // non nullable, une application qui ne l'envoie pas encore le verrait
        // désérialiser à ZÉRO, et chaque paiement échouerait sur un écart de montant
        // incompréhensible — « attendu 0 ». Nul veut dire « pas envoyé », et se dit
        // comme tel.
        //
        // C'est une rupture de contrat assumée : une application qui n'envoie pas
        // `expectedTotal` ne peut plus payer. Fermé plutôt qu'ouvert, et bruyant plutôt
        // que silencieux — l'inverse laisserait la protection s'éroder au premier client
        // qui oublie le champ.
        // ═════════════════════════════════════════════════════════════════════════
        if (request.ExpectedTotal is not { } expectedTotal)
        {
            return Results.Problem(
                title: "checkout.expected_total_required",
                detail: "Le champ `expectedTotal` est obligatoire : renvoyez le total affiché "
                      + "par /checkout/quote, afin que le serveur refuse de débiter un montant "
                      + "différent de celui que l'acheteur a vu.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var placed = await sender.Send(
            new PlaceOrderCommand(buyerId, shipTo, shippingFee, request.WalletAmount, expectedTotal), ct);
        if (placed.IsFailure)
        {
            return placed.Match(_ => Results.Ok()); // mappe l'erreur (panier vide, stock…)
        }

        var orderId = placed.Value;

        // ─────────────────────────────────────────────────────────────────────────────
        // LA CAGNOTTE A TOUT COUVERT : PAS DE PAIEMENT À INITIER.
        //
        // Le Saga a déjà confirmé la commande — c'est lui qui décide, pas le BFF, et
        // c'est pour cela qu'on RELIT la commande plutôt que de déduire l'issue du
        // montant demandé. Entre-temps la cagnotte a pu être partiellement engagée
        // ailleurs, et la commande garder un reste à payer.
        //
        // La réponse conserve la même forme, `payment` valant null : le client mobile
        // teste ce champ pour savoir s'il doit ouvrir une page de paiement.
        // ─────────────────────────────────────────────────────────────────────────────
        var placedOrder = await sender.Send(new GetOrderQuery(orderId), ct);
        if (placedOrder.IsSuccess && placedOrder.Value.AmountDue <= 0m)
        {
            var settledBody = JsonSerializer.Serialize(
                new { orderId, payment = (object?)null, walletApplied = placedOrder.Value.WalletApplied },
                WebJson);

            if (idempotencyCacheKey is not null)
            {
                await cache.SetStringAsync(
                    idempotencyCacheKey,
                    settledBody,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) },
                    ct);
            }

            return Results.Content(settledBody, "application/json");
        }

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

        var body = JsonSerializer.Serialize(
            new
            {
                orderId,
                payment = payment.Value,
                // Ce qui a été réglé par la cagnotte : le client doit pouvoir expliquer
                // l'écart entre le total de sa commande et le montant que le prestataire
                // lui demande.
                walletApplied = placedOrder.IsSuccess ? placedOrder.Value.WalletApplied : 0m,
            },
            WebJson);

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
    /// Indispensable, et facile à manquer. `Results.Ok` sérialise avec les
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
        if (ClientBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }
        var result = await sender.Send(new GetPaymentQuery(id), ct);
        // 404 si absent OU non possédé (anti-IDOR, pas de divulgation).
        return result.IsSuccess && result.Value.BuyerId == buyerId
            ? Results.Ok(result.Value)
            : Results.NotFound();
    }

    /// <summary>
    /// ─────────────────────────────────────────────────────────────────────────────
    /// OPTIONS DE LIVRAISON POUR UNE DESTINATION.
    ///
    /// La grille codée en dur qui vivait ici a disparu : elle est descendue dans le
    /// module Shipping, où elle sert désormais de dernier recours quand aucune zone
    /// n'est configurée. Ce BFF ne connaît plus aucun prix.
    ///
    /// LA COMMUNE EST UN PARAMÈTRE, ET SON ABSENCE EST LÉGITIME. L'acheteur consulte
    /// souvent les tarifs avant d'avoir choisi son adresse ; il reçoit alors le tarif
    /// de repli. Le prix qui l'engage est celui du devis fait AVEC son adresse, et
    /// c'est `expectedTotal` qui scelle l'accord au moment de payer.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> ShippingOptions(
        string? commune, IShippingModuleApi shipping, CancellationToken ct)
    {
        var options = await shipping.GetRatesForCommuneAsync(commune, ct);

        return Results.Ok(options.Select(r => new
        {
            code = r.ServiceCode,
            label = r.Label,
            eta = r.Eta,
            amount = r.Amount,
            currency = r.Currency,
            zone = r.ZoneName,
        }));
    }

    /// <summary>Devis : sous-total du panier + livraison choisie = total à payer.</summary>
    private static async Task<IResult> QuoteAsync(
        ClaimsPrincipal user, string? shipping, string? commune,
        ISender sender, ISettlementModuleApi settlement, IShippingModuleApi shippingModule, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } buyerId)
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

        // MÊME APPEL, MÊME COMMUNE que dans `PayAsync`. Deux résolutions différentes
        // du même tarif — l'une pour afficher, l'autre pour facturer — étaient
        // exactement le défaut que `expectedTotal` a été posé pour rattraper ; les
        // faire converger sur un seul chemin évite d'avoir à le rattraper.
        var options = await shippingModule.GetRatesForCommuneAsync(commune, ct);
        var rate = options.FirstOrDefault(r => r.ServiceCode == code) ?? options.FirstOrDefault();

        // Un devis sans tarif disponible ne s'invente pas : mieux vaut le dire que
        // d'afficher un total que le paiement refusera ensuite.
        if (rate is null)
        {
            return Results.Problem(
                title: "checkout.shipping_unavailable",
                detail: "Aucun tarif de livraison n'a pu être déterminé pour cette destination.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var total = cart.GrandTotal + rate.Amount;

        // Solde DISPONIBLE, hors montants déjà engagés sur une autre commande en cours :
        // afficher un montant que l'acheteur ne peut pas utiliser garantit une déception
        // au moment de payer.
        var walletAvailable = await settlement.GetSpendableCreditAsync(buyerId, ct);

        return Results.Ok(new
        {
            subtotal = cart.Subtotal,
            discount = cart.TotalSellerDiscount + cart.TotalPlatformDiscount,
            shipping = new { code = rate.ServiceCode, label = rate.Label, eta = rate.Eta, amount = rate.Amount, zone = rate.ZoneName },
            shippingOptions = options.Select(r => new { code = r.ServiceCode, label = r.Label, eta = r.Eta, amount = r.Amount }),
            total,
            currency = cart.Currency,
            wallet = new
            {
                available = walletAvailable,
                // Ce que l'acheteur peut au plus appliquer sur CETTE commande. Le calcul
                // est fait ici et pas dans l'application mobile : deux implémentations du
                // même plafond finiraient par diverger, et l'écart se réglerait en argent.
                applicable = Math.Min(walletAvailable, total),
            },
        });
    }

    /// <summary>Liste les adresses de l'acheteur (défaut en tête).</summary>
    private static async Task<IResult> ListAddressesAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListAddressesQuery(userId), ct)).Match(r => Results.Ok(r));

    /// <summary>Ajoute une adresse au carnet.</summary>
    private static async Task<IResult> AddAddressAsync(ClaimsPrincipal user, AddressRequest request, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new AddAddressCommand(
            userId, request.Label, request.Recipient, request.Phone,
            request.Commune, request.Quartier, request.Landmark, request.Line1,
            request.Latitude, request.Longitude,
            request.MakeDefault), ct);
        return result.Match(id => Results.Ok(new { id }));
    }

    /// <summary>Met à jour une adresse du carnet (scopée à l'acheteur du jeton).</summary>
    private static async Task<IResult> UpdateAddressAsync(ClaimsPrincipal user, Guid id, AddressRequest request, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new UpdateAddressCommand(
            userId, id, request.Label, request.Recipient, request.Phone,
            request.Commune, request.Quartier, request.Landmark, request.Line1,
            request.Latitude, request.Longitude,
            request.MakeDefault), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Supprime une adresse du carnet.</summary>
    private static async Task<IResult> DeleteAddressAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new DeleteAddressCommand(userId, id), ct)).Match(() => Results.NoContent());

    /// <summary>Définit l'adresse par défaut.</summary>
    private static async Task<IResult> SetDefaultAddressAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new SetDefaultAddressCommand(userId, id), ct)).Match(() => Results.NoContent());

    public sealed record PayRequest(
        string Method, string Provider, string? Flow, string? ReturnUrl, string? CancelUrl, string? PayerPhone,
        Guid? AddressId = null, string? ShippingCode = null,
        // Part que l'acheteur choisit de régler avec sa cagnotte. Zéro par défaut : elle
        // ne se dépense jamais sans qu'il l'ait demandé. Le serveur la plafonne au solde
        // réellement disponible et au total de la commande.
        decimal WalletAmount = 0m,
        /// <summary>
        /// Le total renvoyé par `/checkout/quote`, réexpédié tel quel. OBLIGATOIRE —
        /// nullable seulement pour pouvoir refuser son absence avec un message qui
        /// l'explique, plutôt que de le voir arriver à zéro et produire un écart de
        /// montant absurde. Voir PayAsync.
        /// </summary>
        decimal? ExpectedTotal = null);

    /// <summary>
    /// Reprise de paiement sur une commande existante. Volontairement dépourvu
    /// d'<c>AddressId</c> et de <c>ShippingCode</c> : l'adresse et les frais de livraison
    /// sont figés sur la commande depuis sa création. Les accepter ici laisserait croire
    /// qu'on peut les changer en réessayant, ce qui modifierait le montant dû après coup.
    /// </summary>
    public sealed record PayOrderRequest(
        string Method, string Provider, string? Flow, string? ReturnUrl, string? CancelUrl, string? PayerPhone);

    /// <summary>
    /// Adresse de livraison. « Commune » et « Landmark » sont obligatoires côté domaine ;
    /// ils sont déclarés nullables ici pour que l'absence produise un message métier clair
    /// plutôt qu'un 400 de désérialisation illisible.
    ///
    /// « Commune » accepte le code (« abomey-calavi ») comme le libellé (« Abomey-Calavi ») :
    /// les applications envoient le code, les imports n'ont que des libellés.
    /// </summary>
    public sealed record AddressRequest(
        string? Label, string? Recipient, string? Phone,
        string? Commune, string? Quartier, string? Landmark, string? Line1,
        // Position FACULTATIVE : absente, l'adresse reste parfaitement livrable.
        double? Latitude, double? Longitude,
        bool MakeDefault);
}
