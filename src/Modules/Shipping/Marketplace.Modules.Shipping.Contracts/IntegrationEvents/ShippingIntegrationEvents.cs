using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Shipping.Contracts.IntegrationEvents;

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

/// <summary>Un point de RAMASSAGE : où le coursier récupère la part d'un vendeur.</summary>
/// <param name="Landmark">
/// Point de repère. C'est le champ qui rend le lieu trouvable — au Bénin, la rue est
/// souvent inexistante, et une adresse sans repère envoie le coursier tourner en rond.
/// </param>
public sealed record DeliveryPickupPointContract(
    Guid ShipmentId,
    Guid SellerId,
    string SellerName,
    string? SellerPhone,
    Guid LocationId,
    string CommuneCode,
    string CommuneName,
    string? Quartier,
    string? Landmark,
    string? Line,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<DeliveryParcelContract> Parcels);

/// <summary>Un article attendu à un point de ramassage.</summary>
public sealed record DeliveryParcelContract(string Sku, int Quantity);

/// <summary>Le point de DÉPÔT : où le coursier livre l'acheteur.</summary>
public sealed record DeliveryDropoffContract(
    Guid BuyerId,
    string? Recipient,
    string? Phone,
    string CommuneCode,
    string CommuneName,
    string? Quartier,
    string? Landmark,
    string? Line,
    double? Latitude,
    double? Longitude);

/// <summary>
/// TOUTES les parts vendeur d'une commande sont préparées : la course peut être créée
/// et offerte aux coursiers.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ÉVÉNEMENT QUI DÉCLENCHE LA LIVRAISON PAR COURSIER.
///
/// Une commande multi-vendeur n'est ramassable que lorsque le DERNIER vendeur a fini.
/// Envoyer un coursier plus tôt, c'est le faire attendre devant une boutique qui n'a
/// rien à lui remettre — et payer cette attente.
///
/// LA CHARGE EST VOLONTAIREMENT COMPLÈTE. Le service coursier est déployé à part et
/// n'accède pas aux bases de la marketplace : lui envoyer des identifiants l'obligerait
/// à rappeler l'API pour chaque adresse, au moment précis où il doit dispatcher vite.
/// Tout ce qu'il faut pour rouler tient dans ce message.
///
/// IDEMPOTENCE À LA CHARGE DU CONSOMMATEUR, sur <paramref name="OrderId"/>. Deux
/// vendeurs qui terminent à la même seconde travaillent dans deux transactions
/// distinctes : chacune peut conclure « tout est prêt » et publier. Une course par
/// commande, pas une par publication.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record OrderReadyForDeliveryIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    /// <summary>Frais de livraison FIGÉS au checkout. Le service décide s'il s'y tient.</summary>
    public required decimal ShippingFee { get; init; }

    public required string Currency { get; init; }

    /// <summary>Un par vendeur et par lieu d'expédition, dans l'ordre où ils ont été créés.</summary>
    public required IReadOnlyList<DeliveryPickupPointContract> Pickups { get; init; }

    public required DeliveryDropoffContract Dropoff { get; init; }
}
