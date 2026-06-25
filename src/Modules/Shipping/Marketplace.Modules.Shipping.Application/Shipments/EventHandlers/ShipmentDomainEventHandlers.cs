using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;
using Marketplace.Modules.Shipping.Domain.Shipments.Events;

namespace Marketplace.Modules.Shipping.Application.Shipments.EventHandlers;

/// <summary>Publie « expédition expédiée » (Notifications : informe l'acheteur du suivi).</summary>
public sealed class ShipmentShippedDomainEventHandler : IDomainEventHandler<ShipmentShippedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ShipmentShippedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(ShipmentShippedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ShipmentShippedIntegrationEvent
            {
                ShipmentId = domainEvent.ShipmentId,
                OrderId = domainEvent.OrderId,
                Carrier = domainEvent.Carrier,
                TrackingNumber = domainEvent.TrackingNumber
            },
            cancellationToken);
}

/// <summary>Publie « expédition livrée » (Notifications / Reviews).</summary>
public sealed class ShipmentDeliveredDomainEventHandler : IDomainEventHandler<ShipmentDeliveredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ShipmentDeliveredDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(ShipmentDeliveredDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ShipmentDeliveredIntegrationEvent { ShipmentId = domainEvent.ShipmentId, OrderId = domainEvent.OrderId, SellerId = domainEvent.SellerId },
            cancellationToken);
}
