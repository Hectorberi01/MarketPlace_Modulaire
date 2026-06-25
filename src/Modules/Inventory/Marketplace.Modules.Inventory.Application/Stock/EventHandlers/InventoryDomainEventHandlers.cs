using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Inventory.Contracts.IntegrationEvents;
using Marketplace.Modules.Inventory.Domain.Stock.Events;

namespace Marketplace.Modules.Inventory.Application.Stock.EventHandlers;

/// <summary>Publie l'IntegrationEvent « stock réservé » (consommé par Ordering).</summary>
public sealed class StockReservedDomainEventHandler : IDomainEventHandler<StockReservedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public StockReservedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(StockReservedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new StockReservedIntegrationEvent
            {
                InventoryItemId = domainEvent.InventoryItemId,
                Sku = domainEvent.Sku,
                OrderId = domainEvent.OrderId,
                Quantity = domainEvent.Quantity
            },
            cancellationToken);
}

/// <summary>Publie l'IntegrationEvent « rupture de stock » (consommé par Offers, Search).</summary>
public sealed class StockDepletedDomainEventHandler : IDomainEventHandler<StockDepletedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public StockDepletedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(StockDepletedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new StockDepletedIntegrationEvent
            {
                InventoryItemId = domainEvent.InventoryItemId,
                Sku = domainEvent.Sku,
                LocationId = domainEvent.LocationId
            },
            cancellationToken);
}
