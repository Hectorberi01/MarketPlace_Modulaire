using MediatR;
using System.Linq;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Shipping.Application.Shipments.Commands;
using Marketplace.Modules.Shipping.Application.Shipments.Queries;
using Marketplace.Modules.Shipping.Contracts;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — exécution. La file est la liste d'expéditions de la boutique du
/// jeton ; chaque accès/mutation par id vérifie l'appartenance (anti-IDOR).
/// </summary>
public static class SellerFulfillmentEndpoints
{
    public static IEndpointRouteBuilder MapSellerFulfillmentEndpoints(this IEndpointRouteBuilder app)
    {
        var ship = app.MapGroup("/seller/shipments").WithTags("Vendeur · Exécution").RequireAuthorization();

        ship.MapGet("/", QueueAsync);
        ship.MapGet("/by-order/{orderId:guid}", ByOrderAsync);
        ship.MapGet("/{id:guid}", GetAsync);
        ship.MapPost("/{id:guid}/prepare", PrepareAsync);
        ship.MapPost("/{id:guid}/ship", ShipAsync);
        ship.MapPost("/{id:guid}/deliver", DeliverAsync);
        ship.MapPost("/{id:guid}/cancel", CancelAsync);

        return app;
    }

    /// <summary>
    /// File d'exécution : toutes les expéditions de la boutique du jeton, enrichies
    /// pour le dashboard (référence commande, nom du client, nombre d'articles).
    /// </summary>
    private static async Task<IResult> QueueAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, IIdentityModuleApi identity, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new ListShipmentsBySellerQuery(seller.Id), ct);
        return result.IsFailure
            ? result.Match(s => Results.Ok(s))
            : Results.Ok(await EnrichAsync(result.Value, identity, ct));
    }

    /// <summary>Expéditions d'une commande, filtrées sur la boutique du jeton (enrichies).</summary>
    private static async Task<IResult> ByOrderAsync(ClaimsPrincipal user, Guid orderId, ISender sender, ISellerModuleApi sellers, IIdentityModuleApi identity, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new ListShipmentsByOrderQuery(orderId), ct);
        return result.IsFailure
            ? result.Match(s => Results.Ok(s))
            : Results.Ok(await EnrichAsync(result.Value.Where(s => s.SellerId == seller.Id), identity, ct));
    }

    /// <summary>
    /// Projette des expéditions vers la forme attendue par le dashboard : référence
    /// commande lisible, nom du client (résolu via Identity), nombre d'articles, et
    /// statut renommé (« Preparing » côté domaine → « Prepared » côté dashboard).
    /// </summary>
    private static async Task<List<object>> EnrichAsync(IEnumerable<ShipmentSummary> shipments, IIdentityModuleApi identity, CancellationToken ct)
    {
        var rows = new List<object>();
        foreach (var s in shipments)
        {
            var customer = "Client";
            var u = await identity.GetUserAsync(s.BuyerId, ct);
            if (u is not null)
            {
                var name = $"{u.FirstName} {u.LastName}".Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    customer = name;
                }
            }

            rows.Add(new
            {
                id = s.Id,
                orderId = s.OrderId,
                orderReference = OrderReference(s.OrderId),
                customer,
                status = DashboardStatus(s.Status),
                carrier = s.Carrier,
                trackingNumber = s.TrackingNumber,
                itemCount = s.Items.Sum(i => i.Quantity),
                createdAt = s.CreatedAtUtc,
            });
        }

        return rows;
    }

    private static string OrderReference(Guid orderId)
    {
        var hex = orderId.ToString("N").ToUpperInvariant();
        return $"CMD-{hex[..8]}";
    }

    /// <summary>Le dashboard nomme le statut « Prepared » là où le domaine dit « Preparing ».</summary>
    private static string DashboardStatus(string backendStatus)
        => string.Equals(backendStatus, "Preparing", StringComparison.OrdinalIgnoreCase) ? "Prepared" : backendStatus;

    private static async Task<IResult> GetAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        return await SellerBff.OwnsShipmentAsync(sender, seller.Id, id, ct)
            ? (await sender.Send(new GetShipmentQuery(id), ct)).Match(s => Results.Ok(s))
            : SellerBff.NotFoundOrForbidden("shipment");
    }

    private static async Task<IResult> PrepareAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct, () => sender.Send(new MarkShipmentPreparingCommand(id), ct));

    private static async Task<IResult> ShipAsync(ClaimsPrincipal user, Guid id, ShipRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct, () => sender.Send(new MarkShipmentShippedCommand(id, request.Carrier, request.TrackingNumber), ct));

    private static async Task<IResult> DeliverAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct, () => sender.Send(new MarkShipmentDeliveredCommand(id), ct));

    private static async Task<IResult> CancelAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct, () => sender.Send(new CancelShipmentCommand(id), ct));

    /// <summary>Résout le vendeur, vérifie la propriété de l'expédition, puis exécute la commande.</summary>
    private static async Task<IResult> GuardAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, Guid shipmentId, CancellationToken ct,
        Func<Task<Marketplace.BuildingBlocks.Domain.Results.Result>> action)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }
        if (!await SellerBff.OwnsShipmentAsync(sender, seller.Id, shipmentId, ct))
        {
            return SellerBff.NotFoundOrForbidden("shipment");
        }

        return (await action()).Match(() => Results.NoContent());
    }

    public sealed record ShipRequest(string Carrier, string TrackingNumber);
}
