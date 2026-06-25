using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Analytics.Domain.Metrics;

namespace Marketplace.Modules.Analytics.Infrastructure.Persistence;

internal sealed class MetricSnapshotRepository : IMetricSnapshotRepository
{
    private readonly AnalyticsDbContext _dbContext;

    public MetricSnapshotRepository(AnalyticsDbContext dbContext) => _dbContext = dbContext;

    public async Task<MetricSnapshot?> GetAsync(MetricScope scope, Guid? ownerId, string periodKey, MetricGranularity granularity, CancellationToken cancellationToken = default)
        => await _dbContext.Snapshots.FirstOrDefaultAsync(
            s => s.Scope == scope && s.OwnerId == ownerId && s.PeriodKey == periodKey && s.Granularity == granularity, cancellationToken);

    public async Task AddAsync(MetricSnapshot snapshot, CancellationToken cancellationToken = default)
        => await _dbContext.Snapshots.AddAsync(snapshot, cancellationToken);

    public async Task<IReadOnlyList<MetricSnapshot>> ListAsync(
        MetricScope scope, Guid? ownerId, MetricGranularity granularity, string fromKey, string toKey, CancellationToken cancellationToken = default)
        => await _dbContext.Snapshots
            .Where(s => s.Scope == scope && s.OwnerId == ownerId && s.Granularity == granularity
                && string.Compare(s.PeriodKey, fromKey) >= 0 && string.Compare(s.PeriodKey, toKey) <= 0)
            .OrderBy(s => s.PeriodKey)
            .ToListAsync(cancellationToken);

    public async Task<bool> IsOrderProcessedAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.ProcessedOrders.AnyAsync(p => p.OrderId == orderId, cancellationToken);

    public async Task MarkOrderProcessedAsync(ProcessedOrder marker, CancellationToken cancellationToken = default)
        => await _dbContext.ProcessedOrders.AddAsync(marker, cancellationToken);
}
