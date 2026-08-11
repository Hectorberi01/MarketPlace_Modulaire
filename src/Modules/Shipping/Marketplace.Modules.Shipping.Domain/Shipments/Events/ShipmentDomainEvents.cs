using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Shipping.Domain.Shipments.Events;

/// <summary>Une expédition a été créée pour un vendeur d'une commande.</summary>
public sealed record ShipmentCreatedDomainEvent(Guid ShipmentId, Guid OrderId, Guid SellerId) : DomainEvent;

/// <summary>
/// Le vendeur a déclaré le colis emballé et prêt à enlever.
///
/// Porte le LIEU D'EXPÉDITION : c'est le point de collecte de la course, et le
/// consommateur ne doit pas avoir à le redemander à Ordering pour le savoir.
/// </summary>
public sealed record ShipmentReadyForPickupDomainEvent(
    Guid ShipmentId, Guid OrderId, Guid SellerId, Guid ShipFromLocationId) : DomainEvent;

/// <summary>L'expédition a été remise au transporteur.</summary>
public sealed record ShipmentShippedDomainEvent(Guid ShipmentId, Guid OrderId, string Carrier, string TrackingNumber) : DomainEvent;

/// <summary>L'expédition a été livrée (escrow + payout du vendeur à libérer).</summary>
public sealed record ShipmentDeliveredDomainEvent(Guid ShipmentId, Guid OrderId, Guid SellerId) : DomainEvent;
