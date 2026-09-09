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

/// <summary>
/// Publie « course en échec » — le message qui sort l'échec du module Shipping.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// SANS CE MAILLON, LA CORRECTION S'ARRÊTERAIT À LA BASE DE DONNÉES.
///
/// Enregistrer l'échec sur l'expédition ne prévient personne : le vendeur ne relit pas
/// sa table `shipments`. Shipping ne peut pas non plus notifier lui-même — il ne connaît
/// ni les comptes, ni les jetons d'appareil, ni les préférences. Il annonce un FAIT, et
/// Notifications décide quoi en dire, comme pour l'expédition et la livraison.
///
/// La publication passe par l'outbox, dans la MÊME TRANSACTION que l'annotation de
/// l'expédition : un échec enregistré dont l'annonce serait perdue ne laisserait rien à
/// réessayer, et on retomberait exactement dans le silence qu'on corrige.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CourierDeliveryFailedDomainEventHandler
    : IDomainEventHandler<CourierDeliveryFailedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public CourierDeliveryFailedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        CourierDeliveryFailedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new CourierDeliveryFailedIntegrationEvent
            {
                ShipmentId = domainEvent.ShipmentId,
                OrderId = domainEvent.OrderId,
                SellerId = domainEvent.SellerId,
                DeliveryJobId = domainEvent.DeliveryJobId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}
