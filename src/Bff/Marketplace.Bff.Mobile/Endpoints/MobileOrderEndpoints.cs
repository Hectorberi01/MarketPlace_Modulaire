using MediatR;
using System.Linq;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
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

    /// <summary>
    /// Liste des commandes de l'utilisateur.
    /// </summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyOrdersQuery(buyerId), ct)).Match(o => Results.Ok(o));

    /// <summary>
    /// Détail commande + expéditions + tracking (1 appel), avec chaque ligne
    /// enrichie du nom et de l'image principale du produit (le résumé commande
    /// ne porte que le SKU).
    /// </summary>
    private static async Task<IResult> DetailAsync(ClaimsPrincipal user, Guid id, ISender sender, IShippingModuleApi shipping, ICatalogModuleApi catalog, CancellationToken ct)
    {
        // Contrôle de propriété : la commande doit appartenir à l'acheteur du jeton.
        var o = await LoadOwnedOrderAsync(user, id, sender, ct);
        if (o is null)
        {
            return Results.NotFound();
        }

        // Séquentiel : le DbContext (scoped) de Catalog n'est pas thread-safe.
        var lines = new List<object>(o.Lines.Count);
        foreach (var l in o.Lines)
        {
            string? name = null;
            string? imageUrl = null;
            var product = await catalog.GetProductAsync(l.ProductId, ct);
            if (product is not null)
            {
                name = product.Name;
                var primary = product.Media.FirstOrDefault(m => m.IsPrimary) ?? product.Media.FirstOrDefault();
                imageUrl = primary?.Url;
            }

            lines.Add(new
            {
                offerId = l.OfferId,
                productId = l.ProductId,
                sku = l.Sku,
                productName = name ?? l.Sku,
                imageUrl,
                quantity = l.Quantity,
                finalUnitPrice = l.FinalUnitPrice,
                lineTotal = l.LineTotal,
            });
        }

        var shipments = await shipping.GetShipmentsByOrderAsync(id, ct);

        return Results.Ok(new
        {
            order = new
            {
                id = o.Id,
                buyerId = o.BuyerId,
                currency = o.Currency,
                status = o.Status,
                createdAtUtc = o.CreatedAtUtc,
                subtotal = o.Subtotal,
                totalSellerDiscount = o.TotalSellerDiscount,
                totalPlatformDiscount = o.TotalPlatformDiscount,
                grandTotal = o.GrandTotal,
                shippingAddress = o.ShippingAddress is null ? null : new
                {
                    label = o.ShippingAddress.Label,
                    recipient = o.ShippingAddress.Recipient,
                    line1 = o.ShippingAddress.Line1,
                    line2 = o.ShippingAddress.Line2,
                    city = o.ShippingAddress.City,
                    country = o.ShippingAddress.Country,
                    phone = o.ShippingAddress.Phone,
                },
                lines,
            },
            shipments,
        });
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
        => MobileBff.CurrentUserId(user) is not { } buyerId
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
    /// Renvoie null si la commande est absente OU ne lui appartient pas (on ne
    /// distingue pas les deux cas : pas de divulgation d'existence, anti-IDOR).
    /// </summary>
    private static async Task<OrderSummary?> LoadOwnedOrderAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } buyerId)
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

    public sealed record MobileOrderDetail(OrderSummary Order, IReadOnlyList<ShipmentSummary> Shipments);
    public sealed record CancelRequest(string? Reason);
    public sealed record ReturnRequest(Guid OfferId, string Reason);
}
