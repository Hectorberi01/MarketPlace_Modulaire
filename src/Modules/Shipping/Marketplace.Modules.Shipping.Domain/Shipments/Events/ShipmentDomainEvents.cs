using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Shipping.Domain.Shipments.Events;

/// <summary>Une expédition a été créée pour un vendeur d'une commande.</summary>
public sealed record ShipmentCreatedDomainEvent(Guid ShipmentId, Guid OrderId, Guid SellerId) : DomainEvent;

/// <summary>L'expédition a été remise au transporteur.</summary>
public sealed record ShipmentShippedDomainEvent(Guid ShipmentId, Guid OrderId, string Carrier, string TrackingNumber) : DomainEvent;

/// <summary>L'expédition a été livrée (escrow + payout du vendeur à libérer).</summary>
public sealed record ShipmentDeliveredDomainEvent(Guid ShipmentId, Guid OrderId, Guid SellerId) : DomainEvent;
