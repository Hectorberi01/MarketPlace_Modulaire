using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Ordering.Application.Orders.Commands;
using Marketplace.Modules.Ordering.Application.Orders.Queries;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Returns.Application.Returns;
using Marketplace.Modules.Shipping.Contracts;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>
/// BFF Mobile — commandes. Le détail agrège la commande et ses expéditions
/// (tracking) en un appel.
/// </summary>
public static class MobileOrderEndpoints
{
    public static IEndpointRouteBuilder MapMobileOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/mobile/orders").WithTags("Mobile · Commandes").RequireAuthorization();

        orders.MapGet("/", ListAsync);
        orders.MapGet("/{id:guid}", DetailAsync);
        orders.MapPost("/{id:guid}/cancel", CancelAsync);
        orders.MapPost("/{id:guid}/return", ReturnAsync);
        orders.MapGet("/{id:guid}/tracking", TrackingAsync);

        return app;
    }

    /// <summary>Liste des commandes de l'utilisateur.</summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyOrdersQuery(buyerId), ct)).Match(o => Results.Ok(o));

    /// <summary>Détail commande + expéditions + tracking (1 appel).</summary>
    private static async Task<IResult> DetailAsync(Guid id, ISender sender, IShippingModuleApi shipping, CancellationToken ct)
    {
        var order = await sender.Send(new GetOrderQuery(id), ct);
        if (order.IsFailure)
        {
            return order.Match(o => Results.Ok(o));
        }

        var shipments = await shipping.GetShipmentsByOrderAsync(id, ct);
        return Results.Ok(new MobileOrderDetail(order.Value, shipments));
    }

    /// <summary>Demande d'annulation (si éligible).</summary>
    private static async Task<IResult> CancelAsync(Guid id, CancelRequest? request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CancelOrderCommand(id, request?.Reason ?? string.Empty), ct)).Match(() => Results.NoContent());

    /// <summary>Ouvre une demande de retour (RMA) sur une ligne de la commande.</summary>
    private static async Task<IResult> ReturnAsync(ClaimsPrincipal user, Guid id, ReturnRequest request, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new RequestReturnCommand(buyerId, id, request.OfferId, request.Reason), ct)).Match(rid => Results.Ok(new { returnRequestId = rid }));

    /// <summary>Suivi d'expédition (expéditions de la commande + statuts).</summary>
    private static async Task<IResult> TrackingAsync(Guid id, IShippingModuleApi shipping, CancellationToken ct)
        => Results.Ok(await shipping.GetShipmentsByOrderAsync(id, ct));

    public sealed record MobileOrderDetail(OrderSummary Order, IReadOnlyList<ShipmentSummary> Shipments);
    public sealed record CancelRequest(string? Reason);
    public sealed record ReturnRequest(Guid OfferId, string Reason);
}
