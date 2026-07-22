using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Infrastructure.Persistence;

internal sealed class OfferRepository : IOfferRepository
{
    private readonly OffersDbContext _dbContext;

    public OfferRepository(OffersDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(Offer offer, CancellationToken cancellationToken = default)
        => await _dbContext.Offers.AddAsync(offer, cancellationToken);

    public void Remove(Offer offer)
        => _dbContext.Offers.Remove(offer);

    public async Task<Offer?> GetByIdAsync(OfferId id, CancellationToken cancellationToken = default)
        => await _dbContext.Offers.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<bool> ExistsAsync(Guid sellerId, string sku, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return false;
        }

        var value = skuResult.Value;
        return await _dbContext.Offers.AnyAsync(o => o.SellerId == sellerId && o.VariantSku == value, cancellationToken);
    }

    public async Task<IReadOnlyList<Offer>> ListByProductAsync(Guid productId, CancellationToken cancellationToken = default)
        => await _dbContext.Offers.Where(o => o.ProductId == productId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Offer>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Offers.Where(o => o.SellerId == sellerId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Offer>> ListExpiredDiscountsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default)
        => await _dbContext.Offers
            .Where(o => o.OriginalSellerPrice != null && o.DiscountEndsOnUtc != null && o.DiscountEndsOnUtc <= nowUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
}
