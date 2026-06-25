using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder;
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

        // Carnet d'adresses, options de livraison géolocalisées et devis final :
        // nécessitent des capacités non encore exposées (carnet d'adresses, calcul
        // de frais géolocalisés, agrégation taxe+frais).
        checkout.MapGet("/addresses", () => MobileBff.NotImplemented("checkout.addresses"));
        checkout.MapPost("/addresses", () => MobileBff.NotImplemented("checkout.addresses"));
        checkout.MapPost("/shipping-options", () => MobileBff.NotImplemented("shipping.rate_calculation"));
        checkout.MapPost("/quote", () => MobileBff.NotImplemented("checkout.quote"));

        // Polling du statut de paiement (anonyme : appelé en boucle par le client).
        app.MapGet("/mobile/payments/{id:guid}/status", PaymentStatusAsync)
            .WithTags("Mobile · Checkout").RequireAuthorization();

        return app;
    }

    /// <summary>Crée la commande depuis le panier actif puis initie le paiement (1 appel).</summary>
    private static async Task<IResult> PayAsync(ClaimsPrincipal user, PayRequest request, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var placed = await sender.Send(new PlaceOrderCommand(buyerId), ct);
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

    /// <summary>Statut courant du paiement (polling).</summary>
    private static async Task<IResult> PaymentStatusAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPaymentQuery(id), ct)).Match(p => Results.Ok(p));

    public sealed record PayRequest(
        string Method, string Provider, string? Flow, string? ReturnUrl, string? CancelUrl, string? PayerPhone);
}
