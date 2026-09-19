namespace Marketplace.Modules.Delivery.Infrastructure.Dispatch;

/// <summary>
/// Le canal sortant vers le SERVICE DE DISPATCH.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE PORT EST DÉCLARÉ DANS L'INFRASTRUCTURE, ET NON DANS L'APPLICATION.
///
/// La règle habituelle veut qu'un port vive dans la couche application, avec son
/// implémentation en infrastructure. Ici, personne dans l'application ne l'appelle : les
/// seuls appelants sont les gestionnaires d'événements d'intégration voisins, qui
/// consomment les contrats de Shipping et d'Ordering. Or le test d'architecture
/// `Delivery_ne_depend_d_AUCUN_autre_module` interdit à Delivery.Application de connaître
/// ces modules — à raison : le module doit rester mince.
///
/// Le port reste donc utile — il découple les gestionnaires du transport HTTP, et le jour
/// où ce canal deviendra un producteur Kafka, seule l'implémentation changera — mais il
/// vit là où il est réellement employé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IDispatchServiceClient
{
    /// <summary>Une commande est prête : le service crée la course et lance les offres.</summary>
    Task SendOrderReadyAsync(DispatchOrderReady payload, CancellationToken cancellationToken = default);

    /// <summary>La commande est annulée : la course tombe, le coursier assigné est libéré.</summary>
    Task SendOrderCancelledAsync(DispatchOrderCancelled payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Le droit de travailler d'un coursier a changé. C'est aussi par là qu'il ENTRE dans
    /// le service de dispatch : il n'y a pas d'inscription séparée.
    /// </summary>
    Task SendCourierStandingAsync(DispatchCourierStanding payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// LA SEULE LECTURE DE CE PORT : où sont les coursiers, maintenant.
    ///
    /// Les trois méthodes ci-dessus ANNONCENT, et sont appelées par le drain d'outbox.
    /// Celle-ci DEMANDE, et est appelée par un écran. La différence a une conséquence
    /// concrète, portée par l'implémentation : le budget d'attente n'est pas le même.
    /// Une annonce peut attendre dix secondes, personne ne regarde ; une lecture qui
    /// alimente une carte rafraîchie toutes les quinze secondes, non.
    ///
    /// ELLE LÈVE COMME LES AUTRES en cas d'échec. Ce n'est pas ici qu'on décide de
    /// dégrader — ce port dit ce que le service a répondu, ou pourquoi il n'a pas pu.
    /// C'est l'API publique du module qui traduit l'échec en instantané injoignable,
    /// parce que c'est elle qui connaît son appelant.
    /// </summary>
    Task<DispatchFleet> GetCourierPositionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Où en est la livraison d'UNE commande, et où est le coursier qui la porte.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// NULL N'EST PAS UNE ERREUR ICI, ET C'EST LE CAS LE PLUS FRÉQUENT.
    ///
    /// Une commande partie par transporteur n'a pas de course. Une commande encore en
    /// préparation non plus. Le service répond 404, et ce 404 se traduit en `null` —
    /// jamais en exception. Confondre « rien à suivre » et « le service est en panne »
    /// afficherait à l'acheteur un message d'incident pour un colis qui voyage très bien.
    ///
    /// Les VRAIS échecs — service injoignable, signature refusée, réponse illisible —
    /// lèvent, comme pour les autres méthodes de ce port.
    /// ═════════════════════════════════════════════════════════════════════════════
    ///
    /// L'ACHETEUR PART DANS LE CORPS, ET C'EST POUR ÇA QUE C'EST UN POST. La signature
    /// porte « horodatage.corps », jamais le chemin : en GET, l'identifiant de commande
    /// et celui de l'acheteur seraient hors de ce qu'elle atteste. Voir le commentaire de
    /// la route côté dispatch, qui développe.
    /// </summary>
    Task<DispatchOrderTracking?> GetOrderTrackingAsync(
        Guid orderId, Guid buyerId, CancellationToken cancellationToken = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// LES CHARGES ENVOYÉES — la forme attendue par le service, pas celle de nos événements.
//
// Elles ressemblent aux événements d'intégration, et c'est normal : c'est ce qu'on
// traduit. Mais ce sont des types SÉPARÉS, et la traduction est explicite. Deux systèmes
// déployés séparément évoluent à des rythmes différents ; sérialiser l'événement tel quel
// ferait de chaque renommage de champ une panne de production silencieuse.
//
// `LocationId` et `CommuneName`, que l'événement porte, ne sont pas transmis : le
// dispatch ne s'en sert pas. On n'envoie pas ce qu'on ne nous demande pas.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record DispatchOrderReady(
    Guid OrderId,
    decimal ShippingFee,
    string Currency,
    IReadOnlyList<DispatchPickup> Pickups,
    DispatchDropoff Dropoff);

public sealed record DispatchPickup(
    Guid ShipmentId,
    Guid SellerId,
    string SellerName,
    string? SellerPhone,
    string CommuneCode,
    string? Quartier,
    string? Landmark,
    string? Line,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<DispatchParcel> Parcels);

public sealed record DispatchParcel(string Sku, int Quantity);

public sealed record DispatchDropoff(
    Guid BuyerId,
    string? Recipient,
    string? Phone,
    string CommuneCode,
    string? Quartier,
    string? Landmark,
    string? Line,
    double? Latitude,
    double? Longitude);

public sealed record DispatchOrderCancelled(Guid OrderId, string Reason);

/// <param name="CourierId">Identifiant de BÉNÉFICIAIRE : celui qui désigne le portefeuille à créditer.</param>
/// <param name="UserId">
/// Identifiant du COMPTE : celui que porte le jeton JWT du coursier, et par lequel le
/// service de dispatch le reconnaît quand il appelle. Les deux sont nécessaires, et ce
/// sont deux Guid différents — l'un fait payer, l'autre authentifie.
/// </param>
/// <param name="Standing">« Pending », « Active » ou « Suspended ». Le service refuse toute autre valeur.</param>
/// <param name="OccurredOnUtc">
/// Instant où la décision a été PRISE ici, repris de l'événement d'intégration — jamais
/// recalculé à l'envoi.
///
/// Le service de dispatch s'en sert pour ordonner deux annonces contradictoires sans se
/// fier à l'ordre d'arrivée : l'outbox réessaie avec un délai croissant, donc un
/// « Active » en échec peut être livré APRÈS le « Suspended » qui l'a suivi. Le poser à
/// DateTime.UtcNow au moment de l'envoi effacerait précisément l'information qui permet
/// de trancher.
/// </param>
public sealed record DispatchCourierStanding(
    Guid CourierId, Guid UserId, string DisplayName, string Standing, DateTime OccurredOnUtc);

/// <summary>
/// La flotte telle que le service la rend : les coursiers ET les deux seuils de fraîcheur
/// avec lesquels il faut les lire. Les seuils viennent de LÀ-BAS parce que c'est là-bas
/// qu'ils sont appliqués — les recopier ici garantirait qu'ils divergent.
/// </summary>
public sealed record DispatchFleet(
    int OfferMaxAgeSeconds,
    int ActionMaxAgeSeconds,
    IReadOnlyList<DispatchCourierPosition> Couriers);

/// <summary>
/// Une position telle que le service de dispatch la rend.
///
/// Type SÉPARÉ de <c>CourierPosition</c> des contrats, pour la raison donnée en tête de
/// ce fichier : deux systèmes déployés séparément évoluent à des rythmes différents. La
/// traduction tient en une ligne, et le jour où le dispatch renomme un champ, la
/// correction est ici plutôt que dans un contrat public.
/// </summary>
public sealed record DispatchCourierPosition(
    Guid CourierId,
    string DisplayName,
    string Vehicle,
    string Availability,
    double Latitude,
    double Longitude,
    DateTime PositionAtUtc);

/// <summary>Ce que la route de suivi reçoit : quelle commande, pour quel acheteur.</summary>
public sealed record DispatchOrderTrackingRequest(Guid OrderId, Guid BuyerId);

/// <summary>
/// Le suivi tel que le service de dispatch le rend.
///
/// Type SÉPARÉ du contrat public, pour la raison donnée en tête de ce fichier. Les points
/// de ramassage n'y figurent que par leur COMPTE : le service ne rend pas leurs
/// coordonnées, et ce module n'a donc rien à décider à ce sujet.
/// </summary>
/// <param name="Leg">« ToPickup », « ToBuyer » ou « None ». Déduit là-bas, jamais ici.</param>
/// <param name="PositionMaxAgeSeconds">
/// Âge au-delà duquel le dispatch lui-même ne croit plus le coursier là où il dit être.
/// Il vient de là-bas parce que c'est là-bas qu'il s'applique.
/// </param>
public sealed record DispatchOrderTracking(
    Guid OrderId,
    Guid JobId,
    string Status,
    string Leg,
    int PickupsDone,
    int PickupsTotal,
    double? DropoffLatitude,
    double? DropoffLongitude,
    DateTime? AcceptedAtUtc,
    DateTime? CompletedAtUtc,
    int PositionMaxAgeSeconds,
    DispatchTrackedCourier? Courier);

/// <summary>Le coursier tel que le suivi le rend — sans son identifiant ni sa disponibilité.</summary>
public sealed record DispatchTrackedCourier(
    string DisplayName,
    string Vehicle,
    double Latitude,
    double Longitude,
    DateTime PositionAtUtc);
