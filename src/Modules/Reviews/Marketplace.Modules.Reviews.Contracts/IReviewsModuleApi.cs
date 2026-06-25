namespace Marketplace.Modules.Reviews.Contracts;

/// <summary>
/// API in-process publique du module Reviews. Permet à Catalog / Search de lire
/// la note agrégée d'un produit sans accéder à sa base.
/// </summary>
public interface IReviewsModuleApi
{
    Task<ProductRatingSummary> GetProductRatingAsync(Guid productId, CancellationToken cancellationToken = default);
}
