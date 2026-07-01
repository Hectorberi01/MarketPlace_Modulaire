namespace Marketplace.Modules.Billing.Domain.Commissions;

public interface ICommissionRuleRepository
{
    Task AddAsync(CommissionRule rule, CancellationToken cancellationToken = default);

    void Remove(CommissionRule rule);

    Task<CommissionRule?> GetByIdAsync(CommissionRuleId id, CancellationToken cancellationToken = default);

    /// <summary>Règles candidates (Seller ciblé, Category ciblée, ou Global) pour la résolution.</summary>
    Task<IReadOnlyList<CommissionRule>> GetCandidatesAsync(Guid sellerId, Guid categoryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommissionRule>> ListAsync(CancellationToken cancellationToken = default);
}
