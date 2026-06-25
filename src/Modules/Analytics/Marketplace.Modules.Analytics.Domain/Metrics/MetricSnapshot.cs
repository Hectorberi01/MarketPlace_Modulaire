namespace Marketplace.Modules.Analytics.Domain.Metrics;

/// <summary>Périmètre d'agrégation des métriques.</summary>
public enum MetricScope
{
    Platform = 0,
    Seller = 1
}

/// <summary>Granularité temporelle d'un instantané.</summary>
public enum MetricGranularity
{
    Day = 0,
    Month = 1
}

/// <summary>
/// Instantané agrégé de métriques (GMV, nombre de commandes, articles vendus)
/// pour un périmètre (plateforme ou vendeur), une période et une granularité.
/// Read model accumulé hors du chemin transactionnel via les événements de
/// commande ; jamais source de vérité financière (cf. Settlement / Billing).
/// </summary>
public sealed class MetricSnapshot
{
    private MetricSnapshot()
    {
    }

    private MetricSnapshot(Guid id, MetricScope scope, Guid? ownerId, string periodKey, MetricGranularity granularity, string currency)
    {
        Id = id;
        Scope = scope;
        OwnerId = ownerId;
        PeriodKey = periodKey;
        Granularity = granularity;
        Currency = currency;
        OrdersCount = 0;
        ItemsSold = 0;
        GmvAmount = 0m;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public MetricScope Scope { get; private set; }
    public Guid? OwnerId { get; private set; }
    public string PeriodKey { get; private set; } = default!;
    public MetricGranularity Granularity { get; private set; }
    public string Currency { get; private set; } = default!;
    public int OrdersCount { get; private set; }
    public int ItemsSold { get; private set; }
    public decimal GmvAmount { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static MetricSnapshot Create(MetricScope scope, Guid? ownerId, string periodKey, MetricGranularity granularity, string currency)
        => new(Guid.NewGuid(), scope, ownerId, periodKey, granularity, currency);

    /// <summary>Incrémente l'instantané d'une commande (GMV + articles + 1 commande).</summary>
    public void Accrue(decimal gmvAmount, int itemsCount)
    {
        OrdersCount += 1;
        ItemsSold += itemsCount;
        GmvAmount += gmvAmount;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
