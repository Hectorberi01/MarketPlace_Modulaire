using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Tax.Domain.Taxes;

namespace Marketplace.Modules.Tax.Infrastructure.Persistence;

internal sealed class TaxRuleRepository : ITaxRuleRepository
{
    private readonly TaxDbContext _dbContext;

    public TaxRuleRepository(TaxDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(TaxRule rule, CancellationToken cancellationToken = default)
        => await _dbContext.TaxRules.AddAsync(rule, cancellationToken);

    public void Remove(TaxRule rule) => _dbContext.TaxRules.Remove(rule);

    public async Task<TaxRule?> GetByIdAsync(TaxRuleId id, CancellationToken cancellationToken = default)
        => await _dbContext.TaxRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaxRule>> GetCandidatesAsync(string jurisdiction, Guid categoryId, CancellationToken cancellationToken = default)
    {
        var normalized = jurisdiction.Trim().ToUpperInvariant();
        return await _dbContext.TaxRules
            .Where(r => r.IsActive && r.Jurisdiction == normalized && (r.CategoryId == categoryId || r.CategoryId == null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TaxRule>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.TaxRules.OrderByDescending(r => r.EffectiveFromUtc).ToListAsync(cancellationToken);
}
