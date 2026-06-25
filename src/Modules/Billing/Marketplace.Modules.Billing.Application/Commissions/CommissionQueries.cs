using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Billing.Contracts;
using Marketplace.Modules.Billing.Domain.Commissions;

namespace Marketplace.Modules.Billing.Application.Commissions;

/// <summary>Liste les règles de commission (back-office).</summary>
public sealed record ListCommissionRulesQuery : IQuery<IReadOnlyList<CommissionRuleSummary>>;

/// <summary>Calcule la commission applicable à un montant brut (aperçu).</summary>
public sealed record ComputeCommissionQuery(Guid SellerId, Guid CategoryId, decimal GrossAmount, string Currency)
    : IQuery<CommissionResult>;

internal static class CommissionMapper
{
    public static CommissionRuleSummary ToSummary(CommissionRule r) => new(
        r.Id.Value, r.Scope.ToString(), r.TargetId, r.Rate, r.FixedFee, r.Currency, r.MinFee, r.MaxFee, r.EffectiveFromUtc, r.IsActive);
}

internal sealed class ListCommissionRulesQueryHandler : IQueryHandler<ListCommissionRulesQuery, IReadOnlyList<CommissionRuleSummary>>
{
    private readonly ICommissionRuleRepository _repository;

    public ListCommissionRulesQueryHandler(ICommissionRuleRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<CommissionRuleSummary>>> Handle(ListCommissionRulesQuery query, CancellationToken cancellationToken)
    {
        var rules = await _repository.ListAsync(cancellationToken);
        IReadOnlyList<CommissionRuleSummary> summaries = rules.Select(CommissionMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class ComputeCommissionQueryHandler : IQueryHandler<ComputeCommissionQuery, CommissionResult>
{
    private readonly ICommissionRuleRepository _repository;

    public ComputeCommissionQueryHandler(ICommissionRuleRepository repository) => _repository = repository;

    public async Task<Result<CommissionResult>> Handle(ComputeCommissionQuery query, CancellationToken cancellationToken)
    {
        var candidates = await _repository.GetCandidatesAsync(query.SellerId, query.CategoryId, cancellationToken);
        var rule = CommissionResolver.Resolve(candidates, query.SellerId, query.CategoryId, DateTime.UtcNow);

        var commission = rule?.ComputeCommission(query.GrossAmount) ?? 0m;
        var net = query.GrossAmount - commission;
        return new CommissionResult(query.GrossAmount, commission, net, query.Currency, rule?.Id.Value);
    }
}
