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

    public async Task<IReadOnlyList<Promotion>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Promotions.OrderByDescending(p => p.Period.StartUtc).ToListAsync(cancellationToken);
}
