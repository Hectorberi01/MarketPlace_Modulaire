namespace HBA.Deliveries.Contracts;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE LE MODULE DELIVERIES ACCEPTE DE MONTRER.
///
/// Ces types sont le contrat public : ils ne portent AUCUNE entité interne, et
/// leurs énumérations sont des CHAÎNES. Deux raisons, et la seconde compte plus
/// que la première :
///
///   • un consommateur ne doit pas se recompiler parce qu'on a ajouté un état
///     intermédiaire au cycle de vie d'une course ;
///   • ces mêmes formes partent dans les webhooks partenaires. Un entier obligerait
///     chaque intégrateur à maintenir sa propre table de correspondance, et le
///     jour où l'on insère une valeur au milieu de l'énumération, tous se
///     décalent en silence.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Id">Identifiant de la course.</param>
/// <param name="Reference">Référence du donneur d'ordre, telle qu'il l'a fournie.</param>
/// <param name="Source">« HbaExpress », « HbaFood » ou « ExternalPartner ».</param>
/// <param name="Status">État courant, en clair.</param>
/// <param name="DriverName">Livreur en charge, s'il y en a un.</param>
/// <param name="DriverPhone">Son numéro : c'est ce que le client réclame en premier.</param>
public sealed record DeliverySummary(
    Guid Id,
    string Reference,
    string Source,
    string Type,
    string Status,
    string PickupSummary,
    string DropoffSummary,
    string? DriverName,
    string? DriverPhone,
    DateTime CreatedAtUtc,
    DateTime? AcceptedAtUtc,
    DateTime? PickedUpAtUtc,
    DateTime? DeliveredAtUtc);

/// <summary>
/// Suivi temps réel d'une course.
///
/// La position du livreur n'est renseignée que pendant le transport — ni avant
/// l'acceptation, ni après la remise. Ce n'est pas une optimisation : suivre en
/// continu la position d'une personne en dehors de la mission qui la justifie
/// serait une collecte sans finalité.
/// </summary>
public sealed record DeliveryTracking(
    Guid DeliveryId,
    string Status,
    double? DriverLatitude,
    double? DriverLongitude,
    DateTime? PositionReportedAtUtc,
    string? DriverName,
    string? DriverPhone);

/// <summary>
/// API en processus du module Deliveries.
///
/// LECTURE SEULE, et c'est délibéré. Créer une course passe par une commande
/// MediatR — un autre module ne doit pas pouvoir déclencher une livraison par un
/// simple appel de méthode, sans validation ni événement.
/// </summary>
public interface IDeliveryModuleApi
{
    Task<DeliverySummary?> GetAsync(Guid deliveryId, CancellationToken cancellationToken = default);

    /// <summary>Retrouve la course rattachée à une référence de commande.</summary>
    Task<DeliverySummary?> GetByReferenceAsync(string reference, string source, CancellationToken cancellationToken = default);

    Task<DeliveryTracking?> GetTrackingAsync(Guid deliveryId, CancellationToken cancellationToken = default);
}
