namespace Marketplace.Modules.Tax.Domain.Taxes;

public interface ITaxRuleRepository
{
    Task AddAsync(TaxRule rule, CancellationToken cancellationToken = default);

    void Remove(TaxRule rule);

    Task<TaxRule?> GetByIdAsync(TaxRuleId id, CancellationToken cancellationToken = default);

    /// <summary>Règles candidates pour une juridiction (catégorie ciblée ou juridiction entière).</summary>
    Task<IReadOnlyList<TaxRule>> GetCandidatesAsync(string jurisdiction, Guid categoryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaxRule>> ListAsync(CancellationToken cancellationToken = default);
}
