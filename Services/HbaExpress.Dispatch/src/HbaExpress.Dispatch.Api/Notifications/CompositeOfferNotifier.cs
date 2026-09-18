using HbaExpress.Dispatch.Api.Realtime;
using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;

namespace HbaExpress.Dispatch.Api.Notifications;

/// <summary>
/// Les deux canaux d'une offre, derrière un seul port.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// DEUX CANAUX PARCE QU'ILS COUVRENT DEUX SITUATIONS DISJOINTES.
///
/// Le hub couvre l'application OUVERTE : instantané, riche, il porte l'offre entière et
/// sait aussi la retirer. Le push couvre le TÉLÉPHONE RANGÉ : plus lent, réduit à un
/// identifiant, mais c'est le seul qui existe quand l'écran est éteint — et une offre
/// vit vingt-cinq secondes.
///
/// Avec le seul hub, un coursier disponible mais téléphone en poche ne voyait jamais
/// passer les courses. Avec le seul push, l'application ouverte aurait sonné au lieu
/// d'afficher. Il fallait les deux, et il fallait qu'un canal en panne n'empêche pas
/// l'autre : L'ÉCHEC DU HUB N'INTERROMPT PAS LE PUSH.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CompositeOfferNotifier : IOfferNotifier
{
    private readonly SignalROfferNotifier _tempsReel;
    private readonly PushOfferNotifier _push;
    private readonly ILogger<CompositeOfferNotifier> _logger;

    public CompositeOfferNotifier(
        SignalROfferNotifier tempsReel,
        PushOfferNotifier push,
        ILogger<CompositeOfferNotifier> logger)
    {
        _tempsReel = tempsReel;
        _push = push;
        _logger = logger;
    }

    public async Task NotifyOfferAsync(
        CourierId courierId, DeliveryJob job, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
    {
        // LE HUB D'ABORD, parce qu'il est quasi gratuit et qu'un coursier qui regarde son
        // écran doit voir l'offre sans attendre quoi que ce soit d'autre.
        await PousserAsync(
            () => _tempsReel.NotifyOfferAsync(courierId, job, expiresAtUtc, cancellationToken),
            "hub", job.Id, courierId);

        // PUIS LE PUSH, qui rend la main immédiatement (envoi détaché).
        await PousserAsync(
            () => _push.NotifyOfferAsync(courierId, job, expiresAtUtc, cancellationToken),
            "push", job.Id, courierId);
    }

    public Task NotifyOfferWithdrawnAsync(
        CourierId courierId, DeliveryJobId jobId, CancellationToken cancellationToken = default)
        // Le retrait ne concerne que le hub : voir PushOfferNotifier pour la raison.
        => _tempsReel.NotifyOfferWithdrawnAsync(courierId, jobId, cancellationToken);

    /// <summary>
    /// Isole un canal. Sans cela, le premier qui lève emporterait le second — et le
    /// coursier perdrait les deux chemins d'un coup, pour une panne qui n'en touchait
    /// qu'un.
    ///
    /// L'annulation, elle, repasse : elle vient de l'appelant, et elle vaut pour tout le
    /// monde.
    /// </summary>
    private async Task PousserAsync(
        Func<Task> canal,
        string nom,
        DeliveryJobId jobId,
        CourierId courierId)
    {
        try
        {
            await canal();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Course {JobId} : canal « {Canal} » en échec pour le coursier {CourierId}. "
                + "L'autre canal a été tenté, et l'offre reste valable en base.",
                jobId, nom, courierId);
        }
    }
}
