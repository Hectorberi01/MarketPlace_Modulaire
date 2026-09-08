using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;

namespace HbaExpress.Dispatch.Application.Abstractions;

/// <summary>Unité de travail du service.</summary>
public interface IDispatchUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Porte l'offre jusqu'au téléphone du coursier.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UN PORT, PAS UNE IMPLÉMENTATION — ET LA RAISON EST DANS L'ORDRE DES CHOSES.
///
/// Le transport temps réel (SignalR) est ce qui reste à construire. Le domaine et la
/// décision « qui gagne » n'en dépendent pas : une offre existe en base dès qu'elle est
/// créée, et un coursier peut l'accepter par simple appel HTTP même si le message poussé
/// s'est perdu en route.
///
/// C'est ce qui rend le service testable, et utilisable, avant que le hub n'existe.
/// L'inverse — construire le transport d'abord — aurait donné un système dont on ne peut
/// rien affirmer tant que tout est branché.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IOfferNotifier
{
    /// <summary>
    /// Pousse une offre vers un coursier. BEST-EFFORT : un échec ne doit jamais faire
    /// échouer le dispatch — l'offre reste valide en base, et le coursier la verra à sa
    /// prochaine ouverture de l'application.
    /// </summary>
    Task NotifyOfferAsync(CourierId courierId, DeliveryJob job, DateTime expiresAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Prévient qu'une offre n'est plus valable (un autre l'a prise, ou elle a expiré).</summary>
    Task NotifyOfferWithdrawnAsync(CourierId courierId, DeliveryJobId jobId, CancellationToken cancellationToken = default);
}
