using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Ordering.Application.Orders.Commands;
using Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder;
using Marketplace.Modules.Ordering.Application.Orders.Queries;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Ordering : placement de commande (Saga), suivi et
/// transitions (paiement, annulation). Le placement fige les prix et réserve le
/// stock ; la confirmation de paiement solde le stock.
/// </summary>
public static class OrderingEndpoints
{
    /// <summary>Enregistre les routes de commande (acheteur authentifié).</summary>
    public static IEndpointRouteBuilder MapOrderingEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapAuthenticatedGroup("/api/orders").WithTags("Ordering");

        orders.MapPost("/", PlaceAsync).WithName("PlaceOrder");
        orders.MapGet("/", ListMineAsync).WithName("ListMyOrders");
        orders.MapGet("/{id:guid}", GetAsync).WithName("GetOrder");
        orders.MapPost("/{id:guid}/cancel", CancelAsync).WithName("CancelOrder");

        // Confirmation de paiement : simule le webhook PSP — réservé Admin tant que Payments n'existe pas.
        orders.MapPost("/{id:guid}/confirm-payment", ConfirmPaymentAsync)
            .WithName("ConfirmOrderPayment").RequireAuthorization(p => p.RequireRole("Admin"));

        // Confirmation de livraison (logistique) : déclenche escrow + payout vendeur.
        orders.MapPost("/{id:guid}/deliver", DeliverAsync)
            .WithName("MarkOrderDelivered").RequireAuthorization(p => p.RequireRole("Admin"));

        return app;
    }

    /// <summary>Place une commande à partir du panier actif (déclenche le Saga : réservation de stock).</summary>
    private static async Task<IResult> PlaceAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new PlaceOrderCommand(buyerId), ct);
        return result.Match(orderId => Results.Created($"/api/orders/{orderId}", new { orderId }));
    }

    /// <summary>Liste les commandes de l'acheteur connecté.</summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ListMyOrdersQuery(buyerId), ct);
        return result.Match(orders => Results.Ok(orders));
    }

    /// <summary>Récupère une commande par son identifiant (propriétaire ou admin).</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal principal, Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetOrderQuery(id), ct);
        if (result.IsFailure)
        {
            // LES DEUX BRANCHES RÉPONDENT LA MÊME CHOSE, ET C'EST TOUT L'OBJET.
            //
            // Celle-ci rendait un ProblemDetails avec le code d'erreur dans `title`,
            // l'autre un 404 nu. Même statut, corps différents : la ressource absente et
            // la ressource d'autrui restaient distinguables, donc l'anti-IDOR d'en
            // dessous ne servait à rien. `PaymentsEndpoints.InitiateAsync` fait déjà
            // ainsi, avec le raisonnement en commentaire.
            return Results.NotFound();
        }
        // Anti-IDOR : seul le propriétaire (ou un admin) peut lire la commande.
        if (!principal.IsInRole("Admin") && CurrentUserId(principal) != result.Value.BuyerId)
        {
            return Results.NotFound();
        }
        return Results.Ok(result.Value);
    }

    /// <summary>Annule une commande et libère ses réservations (propriétaire ou admin).</summary>
    private static async Task<IResult> CancelAsync(ClaimsPrincipal principal, Guid id, CancelRequest? request, ISender sender, CancellationToken ct)
    {
        var orderResult = await sender.Send(new GetOrderQuery(id), ct);
        if (orderResult.IsFailure)
        {
            // Même correction que sur la lecture ci-dessus — et elle compte davantage
            // ici : c'est une MUTATION, donc un identifiant valide se confirme sans même
            // avoir à lire la commande.
            return Results.NotFound();
        }
        if (!principal.IsInRole("Admin") && CurrentUserId(principal) != orderResult.Value.BuyerId)
        {
            return Results.NotFound();
        }
        var result = await sender.Send(new CancelOrderCommand(id, request?.Reason ?? string.Empty), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Confirme le paiement : solde le stock et confirme la commande.</summary>
    private static async Task<IResult> ConfirmPaymentAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ConfirmOrderPaymentCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Marque la commande livrée : libère l'escrow et déclenche le payout vendeur.</summary>
    private static async Task<IResult> DeliverAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new MarkOrderDeliveredCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public sealed record CancelRequest(string? Reason);
}
