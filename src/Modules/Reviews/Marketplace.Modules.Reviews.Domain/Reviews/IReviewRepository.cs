namespace Marketplace.Modules.Reviews.Domain.Reviews;

/// <summary>Note agrégée d'un produit (moyenne + nombre d'avis publiés).</summary>
public sealed record ProductRating(Guid ProductId, double Average, int Count);

/// <summary>Note agrégée d'un vendeur (moyenne + nombre d'avis publiés sur ses produits).</summary>
public sealed record SellerRating(Guid SellerId, double Average, int Count);

public interface IReviewRepository
{
    Task AddAsync(Review review, CancellationToken cancellationToken = default);

    Task<Review?> GetByIdAsync(ReviewId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Review>> ListByProductAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Liste tous les avis ciblant les produits d'un vendeur (back-office vendeur).</summary>
    Task<IReadOnlyList<Review>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Un acheteur ne peut déposer qu'un avis par produit et par commande.</summary>
    Task<bool> ExistsAsync(Guid buyerId, Guid productId, Guid orderId, CancellationToken cancellationToken = default);

    Task<ProductRating> GetProductRatingAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Note agrégée sur TOUS les produits d'un vendeur (avis publiés).</summary>
    Task<SellerRating> GetSellerRatingAsync(Guid sellerId, CancellationToken cancellationToken = default);
}
