using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Pricing.Domain.Promotions;

namespace Marketplace.Modules.Pricing.Infrastructure.Persistence;

internal sealed class PromotionRepository : IPromotionRepository
{
    private readonly PricingDbContext _dbContext;

    public PromotionRepository(PricingDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(Promotion promotion, CancellationToken cancellationToken = default)
        => await _dbContext.Promotions.AddAsync(promotion, cancellationToken);

    public void Remove(Promotion promotion)
        => _dbContext.Promotions.Remove(promotion);

    public async Task<Promotion?> GetByIdAsync(PromotionId id, CancellationToken cancellationToken = default)
        => await _dbContext.Promotions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Promotion>> GetActiveCandidatesAsync(
        Guid productId, Guid categoryId, Guid sellerId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        // On charge les promotions actives dans la période ; le filtrage fin par
        // périmètre/cibles/conditions est fait par le PriceCalculator (AppliesTo).
        return await _dbContext.Promotions
            .Where(p => p.Status == PromotionStatus.Active
                        && p.Period.StartUtc <= nowUtc
                        && p.Period.EndUtc >= nowUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<Promotion?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await _dbContext.Promotions.FirstOrDefaultAsync(p => p.Code == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<Promotion>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Promotions.OrderByDescending(p => p.Period.StartUtc).ToListAsync(cancellationToken);
}

internal sealed class PromotionRedemptionRepository : IPromotionRedemptionRepository
{
    private readonly PricingDbContext _dbContext;

    public PromotionRedemptionRepository(PricingDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(PromotionRedemption redemption, CancellationToken cancellationToken = default)
        => await _dbContext.PromotionRedemptions.AddAsync(redemption, cancellationToken);

    public async Task<bool> ExistsAsync(PromotionId promotionId, Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.PromotionRedemptions
            .AnyAsync(r => r.PromotionId == promotionId && r.OrderId == orderId, cancellationToken);

    public async Task<int> CountForBuyerAsync(PromotionId promotionId, Guid buyerId, CancellationToken cancellationToken = default)
        => await _dbContext.PromotionRedemptions
            .CountAsync(r => r.PromotionId == promotionId && r.BuyerId == buyerId, cancellationToken);
}
