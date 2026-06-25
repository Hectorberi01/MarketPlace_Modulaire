using Marketplace.Modules.Billing.Contracts;
using Marketplace.Modules.Billing.Domain.Commissions;

namespace Marketplace.Modules.Billing.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Billing : résout la
/// règle de commission la plus spécifique et calcule la part plateforme.
/// </summary>
internal sealed class CommissionModuleApi : ICommissionModuleApi
{
    private readonly ICommissionRuleRepository _repository;

    public CommissionModuleApi(ICommissionRuleRepository repository) => _repository = repository;

    public async Task<CommissionResult> ComputeCommissionAsync(
        Guid sellerId, Guid categoryId, decimal grossAmount, string currency, CancellationToken cancellationToken = default)
    {
        var candidates = await _repository.GetCandidatesAsync(sellerId, categoryId, cancellationToken);
        var rule = CommissionResolver.Resolve(candidates, sellerId, categoryId, DateTime.UtcNow);

        var commission = rule?.ComputeCommission(grossAmount) ?? 0m;
        return new CommissionResult(grossAmount, commission, grossAmount - commission, currency, rule?.Id.Value);
    }
}
