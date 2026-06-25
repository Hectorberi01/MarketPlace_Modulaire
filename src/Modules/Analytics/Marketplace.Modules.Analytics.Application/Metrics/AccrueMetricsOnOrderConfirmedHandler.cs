using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Analytics.Domain.Metrics;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Analytics.Application.Metrics;

/// <summary>
/// À la confirmation d'une commande, accumule les métriques (GMV, commandes,
/// articles) dans les instantanés plateforme et par vendeur, en granularité
/// jour et mois. Idempotent par commande (marqueur ProcessedOrder).
/// </summary>
public sealed class AccrueMetricsOnOrderConfirmedHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly IMetricSnapshotRepository _repository;
    private readonly IOrderingModuleApi _orderingModuleApi;
    private readonly IAnalyticsUnitOfWork _unitOfWork;

    public AccrueMetricsOnOrderConfirmedHandler(
        IMetricSnapshotRepository repository,
        IOrderingModuleApi orderingModuleApi,
        IAnalyticsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _orderingModuleApi = orderingModuleApi;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await _repository.IsOrderProcessedAsync(integrationEvent.OrderId, cancellationToken))
        {
            return;
        }

        var order = await _orderingModuleApi.GetOrderAsync(integrationEvent.OrderId, cancellationToken);
        if (order is null || order.Lines.Count == 0)
        {
            return;
        }

        var dayKey = order.CreatedAtUtc.ToString("yyyy-MM-dd");
        var monthKey = order.CreatedAtUtc.ToString("yyyy-MM");
        var platformItems = order.Lines.Sum(l => l.Quantity);

        await AccrueAsync(MetricScope.Platform, null, dayKey, MetricGranularity.Day, order.Currency, order.GrandTotal, platformItems, cancellationToken);
        await AccrueAsync(MetricScope.Platform, null, monthKey, MetricGranularity.Month, order.Currency, order.GrandTotal, platformItems, cancellationToken);

        foreach (var sellerGroup in order.Lines.GroupBy(l => l.SellerId))
        {
            var sellerGmv = sellerGroup.Sum(l => l.LineTotal);
            var sellerItems = sellerGroup.Sum(l => l.Quantity);
            await AccrueAsync(MetricScope.Seller, sellerGroup.Key, dayKey, MetricGranularity.Day, order.Currency, sellerGmv, sellerItems, cancellationToken);
            await AccrueAsync(MetricScope.Seller, sellerGroup.Key, monthKey, MetricGranularity.Month, order.Currency, sellerGmv, sellerItems, cancellationToken);
        }

        await _repository.MarkOrderProcessedAsync(ProcessedOrder.Mark(order.Id), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task AccrueAsync(
        MetricScope scope, Guid? ownerId, string periodKey, MetricGranularity granularity, string currency, decimal gmv, int items, CancellationToken ct)
    {
        var snapshot = await _repository.GetAsync(scope, ownerId, periodKey, granularity, ct);
        if (snapshot is null)
        {
            snapshot = MetricSnapshot.Create(scope, ownerId, periodKey, granularity, currency);
            await _repository.AddAsync(snapshot, ct);
        }

        snapshot.Accrue(gmv, items);
    }
}
