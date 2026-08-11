using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;
using Marketplace.Modules.Shipping.Domain.Shipments.Events;

namespace Marketplace.Modules.Shipping.Application.Shipments.EventHandlers;

/// <summary>
/// Publie « colis prêt à enlever ».
///
/// Shipping annonce un FAIT — le colis est emballé — et ignore qui l'écoute. Ce
/// n'est pas une précaution de style : c'est ce qui permet à HBA Delivery de
/// rester un module qu'on peut extraire et vendre, et à Shipping de continuer de
/// fonctionner le jour où les colis partiraient par un transporteur externe.
/// </summary>
public sealed class ShipmentReadyForPickupDomainEventHandler : IDomainEventHandler<ShipmentReadyForPickupDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ShipmentReadyForPickupDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(ShipmentReadyForPickupDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ShipmentReadyForPickupIntegrationEvent
            {
                ShipmentId = domainEvent.ShipmentId,
                OrderId = domainEvent.OrderId,
                SellerId = domainEvent.SellerId,
                ShipFromLocationId = domainEvent.ShipFromLocationId
            },
            cancellationToken);
}

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
