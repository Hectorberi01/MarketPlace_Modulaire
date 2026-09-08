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
public sealed record DispatchCourierStanding(Guid CourierId, Guid UserId, string DisplayName, string Standing);
