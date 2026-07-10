using MediatR;
using System.Security.Claims;
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

    /// <summary>Confirme un paiement au retour d'une page hébergée (interroge le PSP).</summary>
    private static async Task<IResult> ConfirmRedirectAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ConfirmPaymentFromRedirectCommand(id), ct)).Match(() => Results.Ok(new { paymentId = id }));

    /// <summary>Crée la commande depuis le panier actif puis initie le paiement (1 appel).</summary>
    private static async Task<IResult> PayAsync(ClaimsPrincipal user, PayRequest request, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
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

        return payment.Match(result => Results.Ok(new { orderId, payment = result }));
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
