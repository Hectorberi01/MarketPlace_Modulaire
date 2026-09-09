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

/// <summary>
/// UNE COURSE A ÉCHOUÉ : le colis n'est jamais parti. Consommé par Notifications, qui
/// prévient le VENDEUR — le seul qui puisse repartir de là.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UN ÉVÉNEMENT DISTINCT, ET PAS UN CHAMP SUR « EXPÉDITION LIVRÉE ».
///
/// `ShipmentDeliveredIntegrationEvent` déclenche une chaîne entière — avis produit,
/// séquestre libérée, gains vendeur rendus retirables. Y faire passer un échec, avec un
/// booléen « en fait non », obligerait CHAQUE consommateur actuel et futur à se souvenir
/// de le tester. Celui qui oublierait paierait un vendeur pour un colis toujours en
/// boutique, et l'oubli ne se verrait nulle part.
///
/// Un fait différent, un contrat différent : le consommateur qui ne s'y abonne pas
/// n'apprend rien de faux.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record CourierDeliveryFailedIntegrationEvent : IntegrationEvent
{
    public required Guid ShipmentId { get; init; }

    public required Guid OrderId { get; init; }

    /// <summary>Le vendeur à prévenir : il détient le colis, et lui seul peut le relancer.</summary>
    public required Guid SellerId { get; init; }

    /// <summary>
    /// LA CLÉ D'IDEMPOTENCE DU CONSOMMATEUR. L'outbox distribue « au moins une fois » et
    /// autorise dix tentatives : sans elle, une panne dans un consommateur voisin
    /// renverrait au vendeur la même mauvaise nouvelle jusqu'à dix fois.
    /// </summary>
    public required Guid DeliveryJobId { get; init; }

    /// <summary>Motif porté par la course, normalisé en amont. Affiché tel quel au vendeur.</summary>
    public required string Reason { get; init; }
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
