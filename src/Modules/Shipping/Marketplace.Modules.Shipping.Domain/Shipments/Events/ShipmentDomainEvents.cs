using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Shipping.Domain.Shipments.Events;

/// <summary>Une expédition a été créée pour un vendeur d'une commande.</summary>
public sealed record ShipmentCreatedDomainEvent(Guid ShipmentId, Guid OrderId, Guid SellerId) : DomainEvent;

/// <summary>
/// Le vendeur a commencé à préparer sa part de la commande.
///
/// SEUL ÉTAT DU CYCLE QUI N'ÉMETTAIT RIEN, et c'est précisément celui qui déclenche la
/// livraison par coursier : la course ne se crée que lorsque TOUTES les parts d'une
/// commande sont prêtes. Sans cet événement, ce moment n'existait nulle part.
/// </summary>
public sealed record ShipmentPreparingDomainEvent(Guid ShipmentId, Guid OrderId, Guid SellerId) : DomainEvent;

/// <summary>L'expédition a été remise au transporteur.</summary>
public sealed record ShipmentShippedDomainEvent(Guid ShipmentId, Guid OrderId, string Carrier, string TrackingNumber) : DomainEvent;

/// <summary>L'expédition a été livrée (escrow + payout du vendeur à libérer).</summary>
public sealed record ShipmentDeliveredDomainEvent(Guid ShipmentId, Guid OrderId, Guid SellerId) : DomainEvent;
