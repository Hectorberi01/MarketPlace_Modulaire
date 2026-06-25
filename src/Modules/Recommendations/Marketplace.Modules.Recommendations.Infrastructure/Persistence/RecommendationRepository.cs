using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Recommendations.Domain.Recommendations;

namespace Marketplace.Modules.Recommendations.Infrastructure.Persistence;

internal sealed class RecommendationRepository : IRecommendationRepository
{
    private readonly RecommendationsDbContext _dbContext;

    public RecommendationRepository(RecommendationsDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Recommendation recommendation, CancellationToken cancellationToken = default)
        => await _dbContext.Recommendations.AddAsync(recommendation, cancellationToken);

    public async Task<Recommendation?> GetByProductAsync(RecommendationType type, Guid contextProductId, CancellationToken cancellationToken = default)
        => await _dbContext.Recommendations
            .FirstOrDefaultAsync(r => r.Type == type && r.ContextProductId == contextProductId, cancellationToken);

    public async Task<Recommendation?> GetByUserAsync(RecommendationType type, Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Recommendations
            .FirstOrDefaultAsync(r => r.Type == type && r.UserId == userId, cancellationToken);
}
