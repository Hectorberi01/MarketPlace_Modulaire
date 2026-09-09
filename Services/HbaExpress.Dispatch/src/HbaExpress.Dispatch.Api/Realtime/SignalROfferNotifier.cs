using HbaExpress.Dispatch.Api.Endpoints;
using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using Microsoft.AspNetCore.SignalR;

namespace HbaExpress.Dispatch.Api.Realtime;

/// <summary>
/// Pousse les offres par le hub. Remplace <c>LoggingOfferNotifier</c>.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// BEST-EFFORT, ET LE MOT EST À PRENDRE AU SÉRIEUX.
///
/// Rien ici ne doit faire échouer un dispatch. Le contrat du port le dit, et
/// DeliveryJobService avale déjà les exceptions de ce chemin — mais l'implémentation ne
/// doit pas s'appuyer là-dessus pour être négligente : elle envoie, et c'est tout.
///
/// LA MÊME FORME QUE GET /api/courier/offers, délibérément. L'offre poussée et l'offre
/// lue au rattrapage sont le MÊME objet : une seule structure à analyser côté
/// application mobile, et aucune divergence possible entre les deux chemins. Deux formes
/// différentes auraient garanti qu'une des deux finisse par ne plus être testée.
///
/// L'ORDRE COMPTE, ET IL EST TENU AILLEURS : DeliveryJobService persiste l'offre AVANT
/// de la pousser. Une offre poussée mais non enregistrée serait invisible du serveur —
/// le coursier l'accepterait et s'entendrait répondre qu'elle n'existe pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SignalROfferNotifier : IOfferNotifier
{
    private readonly IHubContext<CourierHub> _hub;

    public SignalROfferNotifier(IHubContext<CourierHub> hub) => _hub = hub;

    public Task NotifyOfferAsync(
        CourierId courierId, DeliveryJob job, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
        => _hub.Clients
            .Group(CourierHub.Group(courierId))
            .SendAsync(CourierHub.OfferEvent, job.ToView(courierId), cancellationToken);

    /// <summary>
    /// Retire une offre d'un téléphone : un autre coursier l'a prise, ou elle a expiré.
    ///
    /// Sans cela, le perdant voit une course qui n'existe plus et essaie de l'accepter
    /// pour rien — il reçoit un 409 correct, mais lit une application qui lui propose
    /// des courses fantômes.
    /// </summary>
    public Task NotifyOfferWithdrawnAsync(
        CourierId courierId, DeliveryJobId jobId, CancellationToken cancellationToken = default)
        => _hub.Clients
            .Group(CourierHub.Group(courierId))
            .SendAsync(CourierHub.OfferWithdrawnEvent, new { jobId = jobId.Value }, cancellationToken);
}
