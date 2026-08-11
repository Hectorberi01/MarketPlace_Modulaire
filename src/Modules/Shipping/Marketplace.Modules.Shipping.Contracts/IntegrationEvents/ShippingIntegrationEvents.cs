using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Shipping.Contracts.IntegrationEvents;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE COLIS EST PRÊT, UN LIVREUR PEUT VENIR LE CHERCHER.
///
/// C'est le signal qui déclenche une course HBA Delivery. Il est consommé au
/// COMPOSITION ROOT et non par le module Deliveries : celui-ci ne dépend
/// d'aucun autre module, pas même de leurs Contracts, parce que c'est ce qui le
/// rend extractible et vendable à des marchands tiers. Shipping, de son côté,
/// n'a pas à savoir que HBA Delivery existe — il annonce un fait, pas une
/// intention.
///
/// L'adaptateur qui les relie vit dans Marketplace.Api, qui a déjà le droit de
/// tout connaître. Voir CreateDeliveryOnShipmentReadyHandler.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ShipmentReadyForPickupIntegrationEvent : IntegrationEvent
{
    public required Guid ShipmentId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid SellerId { get; init; }

    /// <summary>Lieu d'expédition : le point de COLLECTE de la course.</summary>
    public required Guid ShipFromLocationId { get; init; }
}

/// <summary>Expédition remise au transporteur. Consommé par Notifications.</summary>
public sealed record ShipmentShippedIntegrationEvent : IntegrationEvent
{
    public required Guid ShipmentId { get; init; }
    public required Guid OrderId { get; init; }
    public required string Carrier { get; init; }
    public required string TrackingNumber { get; init; }
}

/// <summary>Expédition livrée. Consommé par Notifications / Reviews / Settlement (payout vendeur).</summary>
public sealed record ShipmentDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid ShipmentId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid SellerId { get; init; }
}
