using System.Security.Claims;
using MediatR;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Offers.Application.Offers.Queries;
using Marketplace.Modules.Shipping.Application.Shipments.Queries;
using Marketplace.Modules.Returns.Application.Returns;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// Utilitaires partagés des endpoints /seller. La boutique (SellerId) n'est jamais
/// fournie par le client : elle est résolue à partir de l'utilisateur du jeton.
/// Les helpers « Owns… » verrouillent les mutations adressées par id : on vérifie
/// que la ressource appartient bien à la boutique du token avant d'agir (anti-IDOR).
/// </summary>
internal static class SellerBff
{
    /// <summary>Identifiant de l'utilisateur courant, extrait du JWT.</summary>
    public static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>Boutique rattachée à l'utilisateur du jeton (null s'il n'est pas vendeur).</summary>
    public static async Task<SellerSummary?> ResolveSellerAsync(
        ClaimsPrincipal principal, ISellerModuleApi sellers, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return null;
        }

        return await sellers.GetSellerByUserIdAsync(userId, ct);
    }

    /// <summary>403 : l'utilisateur authentifié n'a pas (encore) de boutique.</summary>
    public static IResult NotASeller()
        => Results.Json(new { error = "not_a_seller", hint = "Onboardez la boutique via POST /seller/shop." },
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>404 normalisé : ressource inexistante OU n'appartenant pas au vendeur (on ne distingue pas, anti-énumération).</summary>
    public static IResult NotFoundOrForbidden(string resource)
        => Results.NotFound(new { error = $"{resource}_not_found" });

    /// <summary>Réponse 501 normalisée pour une capacité non encore exposée par un module.</summary>
    public static IResult NotImplemented(string capability)
        => Results.Json(new { error = "not_implemented", capability }, statusCode: StatusCodes.Status501NotImplemented);

    // --- Vérifications de propriété (anti-IDOR) ---

    /// <summary>Vrai si l'offre existe ET appartient au vendeur.</summary>
    public static async Task<bool> OwnsOfferAsync(ISender sender, Guid sellerId, Guid offerId, CancellationToken ct)
    {
        var offer = await sender.Send(new GetOfferQuery(offerId), ct);
        return offer.IsSuccess && offer.Value.SellerId == sellerId;
    }

    /// <summary>Vrai si le produit existe ET appartient au vendeur.</summary>
    public static async Task<bool> OwnsProductAsync(ICatalogModuleApi catalog, Guid sellerId, Guid productId, CancellationToken ct)
    {
        var product = await catalog.GetProductAsync(productId, ct);
        return product is not null && product.SellerId == sellerId;
    }

    /// <summary>Vrai si l'expédition existe ET appartient au vendeur.</summary>
    public static async Task<bool> OwnsShipmentAsync(ISender sender, Guid sellerId, Guid shipmentId, CancellationToken ct)
    {
        var shipment = await sender.Send(new GetShipmentQuery(shipmentId), ct);
        return shipment.IsSuccess && shipment.Value.SellerId == sellerId;
    }

    /// <summary>Vrai si la demande de retour existe ET appartient au vendeur.</summary>
    public static async Task<bool> OwnsReturnAsync(ISender sender, Guid sellerId, Guid returnId, CancellationToken ct)
    {
        var rma = await sender.Send(new GetReturnQuery(returnId), ct);
        return rma.IsSuccess && rma.Value.SellerId == sellerId;
    }

    /// <summary>Vrai si l'avis cible un produit du vendeur.</summary>
    public static async Task<bool> OwnsReviewAsync(ISender sender, Guid sellerId, Guid reviewId, CancellationToken ct)
    {
        var review = await sender.Send(new GetReviewQuery(reviewId), ct);
        return review.IsSuccess && review.Value.SellerId == sellerId;
    }

    /// <summary>Vrai si le vendeur a au moins une expédition rattachée à la commande (preuve d'implication).</summary>
    public static async Task<bool> IsInvolvedInOrderAsync(ISender sender, Guid sellerId, Guid orderId, CancellationToken ct)
    {
        var shipments = await sender.Send(new ListShipmentsByOrderQuery(orderId), ct);
        return shipments.IsSuccess && shipments.Value.Any(s => s.SellerId == sellerId);
    }
}
