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

namespace Marketplace.Bff.Client.Endpoints;

/// <summary>
/// BFF Client — commandes. Le détail agrège la commande et ses expéditions
/// (tracking) en un appel.
/// </summary>
public static class ClientOrderEndpoints
{
    public static IEndpointRouteBuilder MapClientOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/client/orders").WithTags("Client · Commandes").RequireAuthorization();

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
        => ClientBff.CurrentUserId(user) is not { } buyerId
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
                // Vendeur du produit : permet à l'app d'ouvrir la conversation avec LE
                // vendeur de cette commande (bouton « Vendeur » du détail commande).
                sellerId = product?.SellerId,
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
                // Frais de livraison figés à la commande. Ils manquaient dans cette
                // réponse alors que le domaine les porte : l'application affichait donc
                // le total sous le libellé « Sous-total », et l'acheteur voyait un article
                // à 1 150 XOF suivi d'un « sous-total » de 2 650 sans explication. Les
                // 1 500 manquants étaient la livraison, simplement jamais envoyée.
                shippingFee = o.ShippingFee,
                grandTotal = o.GrandTotal,
                shippingAddress = o.ShippingAddress is null ? null : new
                {
                    label = o.ShippingAddress.Label,
                    recipient = o.ShippingAddress.Recipient,
                    phone = o.ShippingAddress.Phone,
                    communeCode = o.ShippingAddress.CommuneCode,
                    // Le LIBELLÉ est envoyé résolu : ni l'application ni la console n'ont à
                    // embarquer les 77 communes pour afficher une commande déjà passée.
                    communeName = o.ShippingAddress.CommuneName,
                    quartier = o.ShippingAddress.Quartier,
                    // Le point de repère : ce que le livreur lit en premier.
                    landmark = o.ShippingAddress.Landmark,
                    line1 = o.ShippingAddress.Line1,
                    countryCode = o.ShippingAddress.CountryCode,
                    // Position figée : elle alimente le lien « ouvrir dans Maps » que le
                    // vendeur transmet au coursier. `null` si l'acheteur n'en avait pas.
                    latitude = o.ShippingAddress.Latitude,
                    longitude = o.ShippingAddress.Longitude,
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
        => ClientBff.CurrentUserId(user) is not { } buyerId
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
        if (ClientBff.CurrentUserId(user) is not { } buyerId)
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

    public sealed record ClientOrderDetail(OrderSummary Order, IReadOnlyList<ShipmentSummary> Shipments);
    public sealed record CancelRequest(string? Reason);
    public sealed record ReturnRequest(Guid OfferId, string Reason);
}
