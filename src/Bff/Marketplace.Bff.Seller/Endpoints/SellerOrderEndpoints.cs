using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Application.Orders.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — commandes. La liste ne renvoie que les commandes du vendeur ; le
/// détail est restreint aux lignes du vendeur (un autre vendeur de la même
/// commande ne doit pas voir ses lignes), avec sous-total recalculé.
/// </summary>
public static class SellerOrderEndpoints
{
    public static IEndpointRouteBuilder MapSellerOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/seller/orders").WithTags("Vendeur · Commandes").RequireAuthorization();

        orders.MapGet("/", ListMineAsync);
        orders.MapGet("/{id:guid}", DetailAsync);

        return app;
    }

    /// <summary>Commandes contenant au moins une ligne du vendeur du jeton.</summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new ListOrdersBySellerQuery(seller.Id), ct);
        return result.Match(list => Results.Ok(list.Select(o => ScopeToSeller(o, seller.Id)).ToList()));
    }

    /// <summary>Détail commande restreint aux lignes du vendeur (404 s'il n'est pas concerné).</summary>
    private static async Task<IResult> DetailAsync(Guid id, ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var order = await sender.Send(new GetOrderQuery(id), ct);
        if (order.IsFailure)
        {
            return order.Match(o => Results.Ok(o));
        }

        if (!order.Value.Lines.Any(l => l.SellerId == seller.Id))
        {
            return SellerBff.NotFoundOrForbidden("order");
        }

        return Results.Ok(ScopeToSeller(order.Value, seller.Id));
    }

    /// <summary>Projette une commande sur les seules lignes du vendeur + totaux recalculés.</summary>
    private static OrderSummary ScopeToSeller(OrderSummary order, Guid sellerId)
    {
        var lines = order.Lines.Where(l => l.SellerId == sellerId).ToList();
        var subtotal = lines.Sum(l => l.LineTotal);
        return order with
        {
            Lines = lines,
            Subtotal = subtotal,
            TotalSellerDiscount = lines.Sum(l => l.SellerDiscount * l.Quantity),
            TotalPlatformDiscount = lines.Sum(l => l.PlatformDiscount * l.Quantity),
            GrandTotal = subtotal
        };
    }
}
