using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Reviews.Domain.Reviews;

namespace Marketplace.Modules.Reviews.Infrastructure.Persistence;

internal sealed class ReviewRepository : IReviewRepository
{
    private readonly ReviewsDbContext _dbContext;

    public ReviewRepository(ReviewsDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Review review, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews.AddAsync(review, cancellationToken);

    public async Task<Review?> GetByIdAsync(ReviewId id, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Review>> ListByProductAsync(Guid productId, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Published)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Review>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews
            .AsNoTracking()
            .Where(r => r.SellerId == sellerId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<bool> ExistsAsync(Guid buyerId, Guid productId, Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews.AnyAsync(
            r => r.BuyerId == buyerId && r.ProductId == productId && r.OrderId == orderId, cancellationToken);

    public async Task<ProductRating> GetProductRatingAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        // Rating est un Value Object converti : on agrège en mémoire pour éviter
        // toute traduction SQL fragile du VO. Volumétrie modérée par produit.
        var ratings = await _dbContext.Reviews
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Published)
            .AsNoTracking()
            .Select(r => r.Rating)
            .ToListAsync(cancellationToken);

        if (ratings.Count == 0)
        {
            return new ProductRating(productId, 0d, 0);
        }

        return new ProductRating(productId, Math.Round(ratings.Average(r => r.Value), 2), ratings.Count);
    }
}
