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

/// <summary>
/// La course d'un coursier a ÉCHOUÉ : le colis est resté chez le vendeur.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE FAIT N'AVAIT AUCUN ÉVÉNEMENT, DONC AUCUN DESTINATAIRE.
///
/// L'échec se terminait dans la base du service de dispatch. Côté marketplace il n'y
/// avait ni transition, ni événement, ni journal : l'expédition restait « en
/// préparation » et le vendeur, qui est le SEUL à pouvoir la relancer par transporteur,
/// n'apprenait rien. Le colis attendait sur une étagère qu'un système croyait en route.
///
/// LE VENDEUR EST LE DESTINATAIRE, PAS L'ACHETEUR. C'est lui qui détient le colis et
/// lui seul qui peut agir — appeler <c>MarkShipped</c> avec un transporteur. Prévenir
/// l'acheteur d'un incident qu'il ne peut pas résoudre, avant même de savoir si le
/// vendeur va réexpédier dans l'heure, produit un appel au support et rien d'autre.
///
/// [SellerId] est PORTÉ PAR L'ÉVÉNEMENT plutôt que relu ensuite : le consommateur vit
/// dans un autre module, et lui faire rouvrir Shipping pour retrouver à qui parler
/// inverserait la dépendance que ce contrat existe justement pour éviter.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record CourierDeliveryFailedDomainEvent(
    Guid ShipmentId,
    Guid OrderId,
    Guid SellerId,
    Guid DeliveryJobId,
    string Reason) : DomainEvent;
