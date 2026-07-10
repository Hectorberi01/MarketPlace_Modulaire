using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Ordering.Application.Orders.Commands;
using Marketplace.Modules.Ordering.Application.Orders.Queries;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Returns.Application.Returns;
using Marketplace.Modules.Shipping.Contracts;

namespace Marketplace.Bff.Web.Endpoints;

/// <summary>
/// BFF Web — commandes. Le détail agrège la commande et ses expéditions
/// (tracking) en un appel.
/// </summary>
public static class WebOrderEndpoints
{
    public static IEndpointRouteBuilder MapWebOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/web/orders").WithTags("Web · Commandes").RequireAuthorization();

        orders.MapGet("/", ListAsync);
        orders.MapGet("/{id:guid}", DetailAsync);
        orders.MapPost("/{id:guid}/cancel", CancelAsync);
        orders.MapPost("/{id:guid}/return", ReturnAsync);
        orders.MapGet("/{id:guid}/tracking", TrackingAsync);

        return app;
    }

    /// <summary>Liste des commandes de l'utilisateur.</summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyOrdersQuery(buyerId), ct)).Match(o => Results.Ok(o));

    /// <summary>Détail commande + expéditions + tracking (1 appel). Réservé au propriétaire.</summary>
    private static async Task<IResult> DetailAsync(ClaimsPrincipal user, Guid id, ISender sender, IShippingModuleApi shipping, CancellationToken ct)
    {
        var order = await LoadOwnedOrderAsync(user, id, sender, ct);
        if (order is null)
        {
            return Results.NotFound();
        }

        var shipments = await shipping.GetShipmentsByOrderAsync(id, ct);
        return Results.Ok(new WebOrderDetail(order, shipments));
    }

    /// <summary>Demande d'annulation (si éligible). Réservée au propriétaire de la commande.</summary>
    private static async Task<IResult> CancelAsync(ClaimsPrincipal user, Guid id, CancelRequest? request, ISender sender, CancellationToken ct)
    {
        if (await LoadOwnedOrderAsync(user, id, sender, ct) is null)
        {
            return Results.NotFound();
        }
        return (await sender.Send(new CancelOrderCommand(id, request?.Reason ?? string.Empty), ct)).Match(() => Results.NoContent());
    }

    /// <summary>Ouvre une demande de retour (RMA) sur une ligne de la commande.</summary>
    private static async Task<IResult> ReturnAsync(ClaimsPrincipal user, Guid id, ReturnRequest request, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new RequestReturnCommand(buyerId, id, request.OfferId, request.Reason), ct)).Match(rid => Results.Ok(new { returnRequestId = rid }));

    /// <summary>Suivi d'expédition (expéditions de la commande + statuts). Réservé au propriétaire.</summary>
    private static async Task<IResult> TrackingAsync(ClaimsPrincipal user, Guid id, ISender sender, IShippingModuleApi shipping, CancellationToken ct)
    {
        if (await LoadOwnedOrderAsync(user, id, sender, ct) is null)
        {
            return Results.NotFound();
        }
        return Results.Ok(await shipping.GetShipmentsByOrderAsync(id, ct));
    }

    /// <summary>
    /// Charge une commande en garantissant qu'elle appartient à l'acheteur du jeton.
    /// Renvoie null si absente OU non possédée (anti-IDOR, pas de divulgation d'existence).
    /// </summary>
    private static async Task<OrderSummary?> LoadOwnedOrderAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
    {
        if (WebBff.CurrentUserId(user) is not { } buyerId)
        {
            return null;
        }
        var result = await sender.Send(new GetOrderQuery(id), ct);
        if (result.IsFailure)
        {
            return null;
        }
        var order = result.Value;
        return order.BuyerId == buyerId ? order : null;
    }

    public sealed record WebOrderDetail(OrderSummary Order, IReadOnlyList<ShipmentSummary> Shipments);
    public sealed record CancelRequest(string? Reason);
    public sealed record ReturnRequest(Guid OfferId, string Reason);
}
