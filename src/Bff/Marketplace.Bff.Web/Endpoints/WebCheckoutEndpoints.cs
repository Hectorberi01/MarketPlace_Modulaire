using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder;
using Marketplace.Modules.Payments.Application.Payments.Commands.InitiatePayment;
using Marketplace.Modules.Payments.Application.Payments.Queries;

namespace Marketplace.Bff.Web.Endpoints;

/// <summary>
/// BFF Web — checkout & paiement. /checkout/pay orchestre la création de la
/// commande (depuis le panier actif) puis l'initiation du paiement en un appel.
/// </summary>
public static class WebCheckoutEndpoints
{
    public static IEndpointRouteBuilder MapWebCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        var checkout = app.MapGroup("/web/checkout").WithTags("Web · Checkout").RequireAuthorization();

        checkout.MapPost("/pay", PayAsync);

        // Carnet d'adresses, options de livraison géolocalisées et devis final :
        // nécessitent des capacités non encore exposées (carnet d'adresses, calcul
        // de frais géolocalisés, agrégation taxe+frais).
        checkout.MapGet("/addresses", () => WebBff.NotImplemented("checkout.addresses"));
        checkout.MapPost("/addresses", () => WebBff.NotImplemented("checkout.addresses"));
        checkout.MapPost("/shipping-options", () => WebBff.NotImplemented("shipping.rate_calculation"));
        checkout.MapPost("/quote", () => WebBff.NotImplemented("checkout.quote"));

        // Polling du statut de paiement (appelé en boucle par le client web après
        // un retour de redirection PSP).
        app.MapGet("/web/payments/{id:guid}/status", PaymentStatusAsync)
            .WithTags("Web · Checkout").RequireAuthorization();

        return app;
    }

    /// <summary>Crée la commande depuis le panier actif puis initie le paiement (1 appel).</summary>
    private static async Task<IResult> PayAsync(ClaimsPrincipal user, PayRequest request, ISender sender, CancellationToken ct)
    {
        if (WebBff.CurrentUserId(user) is not { } buyerId)
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

    /// <summary>Statut courant du paiement (polling). Réservé au propriétaire du paiement.</summary>
    private static async Task<IResult> PaymentStatusAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
    {
        if (WebBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }
        var result = await sender.Send(new GetPaymentQuery(id), ct);
        // 404 si absent OU non possédé (anti-IDOR, pas de divulgation).
        return result.IsSuccess && result.Value.BuyerId == buyerId
            ? Results.Ok(result.Value)
            : Results.NotFound();
    }

    public sealed record PayRequest(
        string Method, string Provider, string? Flow, string? ReturnUrl, string? CancelUrl, string? PayerPhone);
}
