namespace Marketplace.Modules.Analytics.Contracts;

/// <summary>Instantané agrégé de métriques.</summary>
public sealed record MetricSnapshotSummary(
    string Scope, Guid? OwnerId, string PeriodKey, string Granularity, int OrdersCount, int ItemsSold, decimal Gmv, string Currency, DateTime UpdatedAtUtc);

/// <summary>Rapport agrégé sur une plage de périodes (avec totaux).</summary>
public sealed record AnalyticsReport(
    string Scope, Guid? OwnerId, string Granularity, string FromKey, string ToKey,
    int TotalOrders, decimal TotalGmv, IReadOnlyList<MetricSnapshotSummary> Snapshots);
