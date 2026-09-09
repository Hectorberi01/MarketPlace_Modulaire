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
        var orders = app.MapSellerGroup("/seller/orders", "Vendeur · Commandes");

        orders.MapGet("/", ListMineAsync);
        orders.MapGet("/{id:guid}", DetailAsync);

        return app;
    }

    /// <summary>
    /// Commandes du vendeur, avec le nom du client (Identity).
    ///
    /// PAGINÉE (<c>?page=&amp;pageSize=</c>) et filtrable (<c>?search=</c> sur la
    /// référence). Sans paramètre, tout est renvoyé — les appelants existants
    /// (dashboard MAUI) continuent donc de fonctionner.
    ///
    /// La pagination a un second effet, moins visible mais décisif : la résolution
    /// du nom de l'acheteur coûte UNE requête Identity par acheteur. Sur 2 000
    /// commandes, c'était 2 000 requêtes à chaque affichage de liste. On ne résout
    /// désormais que les acheteurs de la page demandée.
    /// </summary>
    private static async Task<IResult> ListMineAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, IIdentityModuleApi identity,
        CancellationToken ct, int? page = null, int? pageSize = null, string? search = null)
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

        var scoped = result.Value
            .Select(o => ScopeToSeller(o, seller.Id))
            // Les plus récentes d'abord : sans tri stable, la pagination renverrait
            // deux fois la même commande sur des pages différentes.
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToList();

        // Recherche par référence (« CMD-A1B2C3D4 » ou juste « a1b2 »). On ne
        // cherche PAS sur le nom du client : cela obligerait à résoudre les 2 000
        // acheteurs avant de filtrer — exactement ce qu'on veut éviter.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim().Replace("CMD-", "", StringComparison.OrdinalIgnoreCase);
            scoped = scoped
                .Where(o => o.Id.ToString("N").StartsWith(needle, StringComparison.OrdinalIgnoreCase)
                            || o.Lines.Any(l => l.Sku.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        scoped = SellerBff.Paginate(scoped, page, pageSize);

        // Nom du client résolu une seule fois par acheteur DE LA PAGE.
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
                phone = scoped.ShippingAddress.Phone,
                communeCode = scoped.ShippingAddress.CommuneCode,
                // Le LIBELLÉ est envoyé résolu : ni l'application ni la console n'ont à
                // embarquer les 77 communes pour afficher une commande déjà passée.
                communeName = scoped.ShippingAddress.CommuneName,
                quartier = scoped.ShippingAddress.Quartier,
                // Le point de repère : ce que le livreur lit en premier.
                landmark = scoped.ShippingAddress.Landmark,
                line1 = scoped.ShippingAddress.Line1,
                countryCode = scoped.ShippingAddress.CountryCode,
                // Position figée : elle alimente le lien « ouvrir dans Maps » que le
                // vendeur transmet au coursier. `null` si l'acheteur n'en avait pas.
                latitude = scoped.ShippingAddress.Latitude,
                longitude = scoped.ShippingAddress.Longitude,
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
            // Montants de LIGNE : plus de multiplication par la quantité.
            TotalSellerDiscount = lines.Sum(l => l.SellerDiscount),
            TotalPlatformDiscount = lines.Sum(l => l.PlatformDiscount),
            GrandTotal = subtotal
        };
    }
}
