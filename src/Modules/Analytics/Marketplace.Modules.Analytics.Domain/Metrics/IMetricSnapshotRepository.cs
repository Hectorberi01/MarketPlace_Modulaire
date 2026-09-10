namespace Marketplace.Modules.Analytics.Domain.Metrics;

public interface IMetricSnapshotRepository
{
    Task<MetricSnapshot?> GetAsync(MetricScope scope, Guid? ownerId, string periodKey, MetricGranularity granularity, CancellationToken cancellationToken = default);

    Task AddAsync(MetricSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Instantanés d'un périmètre sur une plage de clés de période [from, to].</summary>
    Task<IReadOnlyList<MetricSnapshot>> ListAsync(
        MetricScope scope, Guid? ownerId, MetricGranularity granularity, string fromKey, string toKey, CancellationToken cancellationToken = default);

    Task<bool> IsOrderProcessedAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task MarkOrderProcessedAsync(ProcessedOrder marker, CancellationToken cancellationToken = default);

    /// <summary>Ce remboursement a-t-il déjà été imputé ? Voir <see cref="ProcessedRefund"/>.</summary>
    Task<bool> IsRefundProcessedAsync(string source, Guid sourceId, CancellationToken cancellationToken = default);

    Task MarkRefundProcessedAsync(ProcessedRefund marker, CancellationToken cancellationToken = default);

    /// <summary>Total déjà retranché du périmètre plateforme pour cette commande, tous flux confondus.</summary>
    Task<decimal> SumRefundedForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}
