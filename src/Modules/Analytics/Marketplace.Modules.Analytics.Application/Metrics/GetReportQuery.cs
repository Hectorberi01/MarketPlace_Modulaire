using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Analytics.Contracts;
using Marketplace.Modules.Analytics.Domain.Metrics;

namespace Marketplace.Modules.Analytics.Application.Metrics;

/// <summary>
/// Rapport agrégé sur une plage de périodes pour un périmètre (plateforme ou
/// vendeur) et une granularité. Réservé à l'administration ou au vendeur concerné.
/// </summary>
public sealed record GetReportQuery(string Scope, Guid? OwnerId, string Granularity, string FromKey, string ToKey) : IQuery<AnalyticsReport>;

internal static class MetricMapper
{
    public static MetricSnapshotSummary ToSummary(MetricSnapshot s) => new(
        s.Scope.ToString(), s.OwnerId, s.PeriodKey, s.Granularity.ToString(), s.OrdersCount, s.ItemsSold, s.GmvAmount, s.Currency, s.UpdatedAtUtc,
        s.RefundedAmount, s.RefundsCount);
}

internal sealed class GetReportQueryHandler : IQueryHandler<GetReportQuery, AnalyticsReport>
{
    private readonly IMetricSnapshotRepository _repository;

    public GetReportQueryHandler(IMetricSnapshotRepository repository) => _repository = repository;

    public async Task<Result<AnalyticsReport>> Handle(GetReportQuery query, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<MetricScope>(query.Scope, ignoreCase: true, out var scope))
        {
            return Error.Validation("analytics.scope_invalid", "Périmètre inconnu (Platform / Seller).");
        }

        if (!Enum.TryParse<MetricGranularity>(query.Granularity, ignoreCase: true, out var granularity))
        {
            return Error.Validation("analytics.granularity_invalid", "Granularité inconnue (Day / Month).");
        }

        if (scope == MetricScope.Seller && query.OwnerId is null)
        {
            return Error.Validation("analytics.owner_required", "Un identifiant vendeur est requis pour un rapport vendeur.");
        }

        var snapshots = await _repository.ListAsync(scope, query.OwnerId, granularity, query.FromKey, query.ToKey, cancellationToken);

        var summaries = snapshots.Select(MetricMapper.ToSummary).ToList();
        var totalOrders = summaries.Sum(s => s.OrdersCount);
        var totalGmv = summaries.Sum(s => s.Gmv);
        // Le BRUT et le RENDU voyagent séparément : le net se calcule, il ne se stocke
        // pas. Un rapport qui n'aurait porté que le net aurait rendu indistinguables un
        // mois sans remboursement et un mois qui en a beaucoup pour le même solde.
        var totalRefunded = summaries.Sum(s => s.Refunded);
        var totalRefundsCount = summaries.Sum(s => s.RefundsCount);

        return new AnalyticsReport(
            scope.ToString(), query.OwnerId, granularity.ToString(), query.FromKey, query.ToKey, totalOrders, totalGmv, summaries,
            totalRefunded, totalRefundsCount);
    }
}
