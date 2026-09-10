namespace Marketplace.Modules.Analytics.Contracts;

/// <summary>
/// Instantané agrégé de métriques.
///
/// <c>Gmv</c> est le BRUT confirmé sur la période, <c>Refunded</c> ce qui a été rendu
/// au titre des commandes de cette même période, <c>NetGmv</c> la différence. Les trois
/// voyagent ensemble parce qu'ils ne disent pas la même chose : un mois net à 900 000
/// sans remboursement et un mois net à 900 000 sur 1 000 000 vendus sont deux
/// situations commerciales opposées, et l'écran doit pouvoir les distinguer.
/// </summary>
public sealed record MetricSnapshotSummary(
    string Scope, Guid? OwnerId, string PeriodKey, string Granularity, int OrdersCount, int ItemsSold, decimal Gmv, string Currency, DateTime UpdatedAtUtc,
    decimal Refunded = 0m, int RefundsCount = 0)
{
    /// <summary>Valeur nette. Peut être négative : un remboursement sans vente comptée.</summary>
    public decimal NetGmv => Gmv - Refunded;
}

/// <summary>Rapport agrégé sur une plage de périodes (avec totaux).</summary>
public sealed record AnalyticsReport(
    string Scope, Guid? OwnerId, string Granularity, string FromKey, string ToKey,
    int TotalOrders, decimal TotalGmv, IReadOnlyList<MetricSnapshotSummary> Snapshots,
    decimal TotalRefunded = 0m, int TotalRefundsCount = 0)
{
    /// <summary>Chiffre d'affaires net de la fenêtre : vendu moins rendu.</summary>
    public decimal TotalNetGmv => TotalGmv - TotalRefunded;
}
