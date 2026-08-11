using Marketplace.BuildingBlocks.Domain.Events;

namespace HBA.Deliveries.Domain.Deliveries.Events;

/// <summary>
/// Faits métier émis par l'agrégat <see cref="Delivery"/>.
///
/// Ils restent INTERNES au module : leur traduction en événements d'intégration
/// — ceux que consomment Order, Wallet ou un webhook partenaire — se fait en
/// Infrastructure. Le domaine ne connaît ni le bus, ni les abonnés.
/// </summary>
public sealed record DeliveryCreatedDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    DeliveryType Type) : DomainEvent;

/// <summary>Le dispatch a commencé à chercher un livreur.</summary>
public sealed record DeliverySearchingDriverDomainEvent(Guid DeliveryId, int AttemptNumber) : DomainEvent;

/// <summary>Une mission a été proposée à un livreur.</summary>
public sealed record DeliveryAssignedDomainEvent(Guid DeliveryId, Guid DriverId) : DomainEvent;

/// <summary>
/// Le livreur a accepté.
///
/// ⚠️ PORTE LA RÉFÉRENCE ET LA SOURCE, comme tous les événements qui franchiront
/// la frontière du module. L'agrégat les connaît déjà : les omettre obligerait le
/// traducteur en événement d'intégration à relire la course en base — une lecture
/// de plus par événement, et surtout un mode de défaillance nouveau, sur un
/// chemin où l'échec signifie « le client ne sera jamais prévenu ».
/// </summary>
public sealed record DeliveryAcceptedDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    Guid DriverId) : DomainEvent;

/// <summary>
/// Le livreur a refusé. Ce n'est PAS un incident : c'est le fonctionnement normal
/// du dispatch, et l'événement sert à relancer la recherche, pas à alerter.
/// </summary>
public sealed record DeliveryRejectedByDriverDomainEvent(Guid DeliveryId, Guid DriverId, string? Reason) : DomainEvent;

/// <summary>Le colis est pris en charge.</summary>
public sealed record DeliveryPickedUpDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    Guid DriverId) : DomainEvent;

/// <summary>
/// Remise effectuée. C'est cet événement qui déclenche le gain du livreur.
///
/// Il PORTE le montant plutôt que de laisser le consommateur le recalculer : le
/// taux de partage bougera, et un service de paie qui referait le calcul avec le
/// taux du jour paierait les courses anciennes au tarif nouveau.
/// </summary>
public sealed record DeliveryCompletedDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    Guid DriverId,
    DateTime DeliveredAtUtc,
    decimal? DriverEarning,
    string? Currency) : DomainEvent;

/// <summary>Course annulée.</summary>
public sealed record DeliveryCancelledDomainEvent(Guid DeliveryId, string Reference, DeliverySource Source, string? Reason) : DomainEvent;

/// <summary>
/// Aucun livreur n'a répondu après épuisement des tentatives.
///
/// Destiné au DISPATCH HUMAIN, pas au client : la course reste vivante et
/// reprenable. C'est un appel à l'aide, pas un constat d'échec.
/// </summary>
public sealed record DeliveryNoDriverAvailableDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    int Attempts) : DomainEvent;
