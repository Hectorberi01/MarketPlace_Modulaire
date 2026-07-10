using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Identity.Contracts;
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

    /// <summary>Commandes contenant au moins une ligne du vendeur du jeton, avec le nom du client (Identity).</summary>
    private static async Task<IResult> ListMineAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, IIdentityModuleApi identity, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new ListOrdersBySellerQuery(seller.Id), ct);
        if (result.IsFailure)
        {
            return result.Match(list => Results.Ok(list));
        }

        var scoped = result.Value.Select(o => ScopeToSeller(o, seller.Id)).ToList();

        // Nom du client résolu une seule fois par acheteur (repli anonymisé si introuvable).
        var names = new Dictionary<Guid, string>();
        foreach (var buyerId in scoped.Select(o => o.BuyerId).Distinct())
        {
            var u = await identity.GetUserAsync(buyerId, ct);
            var name = u is null ? "" : $"{u.FirstName} {u.LastName}".Trim();
            names[buyerId] = string.IsNullOrWhiteSpace(name) ? "Client" : name;
        }

        var payload = scoped.Select(o => new
        {
            id = o.Id,
            buyerId = o.BuyerId,
            status = o.Status,
            paymentStatus = ToPaymentStatus(o.Status),
            createdAtUtc = o.CreatedAtUtc,
            subtotal = o.Subtotal,
            grandTotal = o.GrandTotal,
            customer = names.TryGetValue(o.BuyerId, out var n) ? n : "Client",
            lines = o.Lines.Select(l => new
            {
                productId = l.ProductId,
                sku = l.Sku,
                quantity = l.Quantity,
                finalUnitPrice = l.FinalUnitPrice,
                lineTotal = l.LineTotal,
            }),
        });

        return Results.Ok(payload);
    }

    /// <summary>
    /// Détail commande restreint aux lignes du vendeur (404 s'il n'est pas concerné),
    /// enrichi pour le dashboard : chaque ligne porte le nom et l'image principale du
    /// produit (via Catalog), la commande porte le nom du client (via Identity) et
    /// l'adresse de livraison figée. Anti-IDOR : on ne projette que les lignes du
    /// vendeur du jeton.
    /// </summary>
    private static async Task<IResult> DetailAsync(
        Guid id, ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers,
        ICatalogModuleApi catalog, IIdentityModuleApi identity, CancellationToken ct)
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

        var scoped = ScopeToSeller(order.Value, seller.Id);
        if (scoped.Lines.Count == 0)
        {
            return SellerBff.NotFoundOrForbidden("order");
        }

        // Nom du client (résolu via Identity, repli anonymisé sinon).
        var customer = "Client";
        var u = await identity.GetUserAsync(scoped.BuyerId, ct);
        if (u is not null)
        {
            var name = $"{u.FirstName} {u.LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                customer = name;
            }
        }

        // Lignes enrichies (séquentiel : le DbContext scoped de Catalog n'est pas thread-safe).
        var lines = new List<object>(scoped.Lines.Count);
        foreach (var l in scoped.Lines)
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

        return Results.Ok(new
        {
            id = scoped.Id,
            buyerId = scoped.BuyerId,
            customer,
            currency = scoped.Currency,
            status = scoped.Status,
            paymentStatus = ToPaymentStatus(scoped.Status),
            createdAtUtc = scoped.CreatedAtUtc,
            subtotal = scoped.Subtotal,
            totalSellerDiscount = scoped.TotalSellerDiscount,
            totalPlatformDiscount = scoped.TotalPlatformDiscount,
            grandTotal = scoped.GrandTotal,
            shippingFee = scoped.ShippingFee,
            shippingAddress = scoped.ShippingAddress is null ? null : new
            {
                label = scoped.ShippingAddress.Label,
                recipient = scoped.ShippingAddress.Recipient,
                line1 = scoped.ShippingAddress.Line1,
                line2 = scoped.ShippingAddress.Line2,
                city = scoped.ShippingAddress.City,
                country = scoped.ShippingAddress.Country,
                phone = scoped.ShippingAddress.Phone,
            },
            lines,
        });
    }

    /// <summary>
    /// Statut de PAIEMENT dérivé du statut de commande Ordering (source de vérité :
    /// l'agrégat Order, dont le cycle de vie est piloté par le Saga —
    /// PaymentCaptured → Paid/Confirmed, PaymentFailed → Cancelled). Champ additif,
    /// distinct de <c>status</c> (statut commande/fulfillment).
    /// Valeurs : "Paid" / "Pending" / "Refunded" / "Failed".
    /// </summary>
    private static string ToPaymentStatus(string? orderStatus) => (orderStatus ?? string.Empty).ToLowerInvariant() switch
    {
        // Payé : le paiement est encaissé (Paid) puis la commande est confirmée/livrée.
        "paid" or "confirmed" or "delivered" => "Paid",
        // Remboursé : pas d'état dédié dans Ordering aujourd'hui (au mieux).
        "refunded" => "Refunded",
        // Échec du paiement / du Saga : annulée ou échouée.
        "failed" or "cancelled" or "canceled" => "Failed",
        // Pending / AwaitingPayment (et tout état antérieur) : paiement non encore encaissé.
        _ => "Pending",
    };

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
