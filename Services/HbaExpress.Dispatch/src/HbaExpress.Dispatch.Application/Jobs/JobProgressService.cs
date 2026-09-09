using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Application.Couriers;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using HbaExpress.Dispatch.Domain.Outbound;
using Marketplace.BuildingBlocks.Domain.Results;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace HbaExpress.Dispatch.Application.Jobs;

/// <summary>
/// La course APRÈS son acceptation : ramassages, transit, livraison, échec, annulation.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CHAQUE MÉTHODE VÉRIFIE QUE L'APPELANT EST LE COURSIER ASSIGNÉ.
///
/// Un jeton valide prouve seulement qu'on est UN coursier, pas qu'on est CELUI de cette
/// course. Sans ce contrôle, n'importe quel coursier connecté pourrait marquer livrée la
/// course d'un autre — et, le jour où le paiement sera branché, déclencher son
/// règlement. C'est la même faille que celle trouvée sur
/// POST /api/shipments/{id}/deliver côté marketplace : une route ouverte à « tout compte
/// authentifié » n'est pas une route protégée.
///
/// Le refus est un 403, pas un 404 : la course existe, elle n'est simplement pas la
/// sienne. Mentir sur son existence ne protégerait rien — l'identifiant lui a été
/// communiqué dans une offre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class JobProgressService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDeliveryJobRepository _jobs;
    private readonly ICourierRepository _couriers;
    private readonly IOutboundMessageRepository _outbound;
    private readonly CourierPayRule _pay;
    private readonly IDispatchUnitOfWork _unitOfWork;
    private readonly ILogger<JobProgressService> _logger;
    private readonly TimeProvider _clock;
    private readonly IDispatchMetrics _metrics;

    public JobProgressService(
        IDeliveryJobRepository jobs,
        ICourierRepository couriers,
        IOutboundMessageRepository outbound,
        CourierPayRule pay,
        IDispatchUnitOfWork unitOfWork,
        ILogger<JobProgressService> logger,
        TimeProvider? clock = null,
        IDispatchMetrics? metrics = null)
    {
        _jobs = jobs;
        _couriers = couriers;
        _outbound = outbound;
        _pay = pay;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _metrics = metrics ?? NullDispatchMetrics.Instance;
    }

    public Task<Result> StartPickupAsync(
        DeliveryJobId jobId, CourierId courierId, CancellationToken cancellationToken = default)
        => MutateAsync(jobId, courierId, job => job.StartPickup(), cancellationToken, requirePosition: true);

    public Task<Result> MarkPickedUpAsync(
        DeliveryJobId jobId, CourierId courierId, Guid pickupPointId, CancellationToken cancellationToken = default)
        => MutateAsync(
            jobId, courierId,
            job => job.MarkPickedUp(pickupPointId, _clock.GetUtcNow().UtcDateTime),
            cancellationToken,
            requirePosition: true);

    public Task<Result> ReportPickupIncidentAsync(
        DeliveryJobId jobId, CourierId courierId, Guid pickupPointId, string reason,
        CancellationToken cancellationToken = default)
        => MutateAsync(jobId, courierId, job => job.ReportPickupIncident(pickupPointId, reason), cancellationToken);

    public Task<Result> StartTransitAsync(
        DeliveryJobId jobId, CourierId courierId, CancellationToken cancellationToken = default)
        => MutateAsync(jobId, courierId, job => job.StartTransit(), cancellationToken, requirePosition: true);

    /// <summary>
    /// Course livrée : la course se ferme, le coursier redevient disponible, et la
    /// rémunération part en file vers la marketplace.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES TROIS TIENNENT DANS UNE SEULE TRANSACTION, ET C'EST TOUT L'ENJEU.
    ///
    /// La fermeture de la course et le message qui la fera payer sont écrits par le même
    /// SaveChanges. Ils existent ensemble ou aucun des deux n'existe. C'est la seule
    /// façon d'exclure le cas qui coûte cher : une course marquée livrée dont l'annonce
    /// s'est perdue, donc un coursier qui a travaillé et que rien ne paiera — sans erreur,
    /// sans trace, sans rien à réessayer.
    ///
    /// L'ENVOI, LUI, EST FAIT PLUS TARD PAR LE DRAIN, autant de fois qu'il le faut. La
    /// marketplace est idempotente sur l'identifiant de course : un doublon ne paie pas
    /// deux fois. Un appel HTTP en ligne ici aurait l'air plus simple et rouvrirait
    /// exactement le trou que cette file referme.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<Result> MarkDeliveredAsync(
        DeliveryJobId jobId, CourierId courierId, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        return await MutateAsync(
            jobId, courierId,
            job => job.MarkDelivered(now),
            cancellationToken,
            release: true,
            requirePosition: true,
            beforeSave: async job =>
            {
                var amount = _pay.AmountFor(job);

                if (amount <= 0m)
                {
                    // Course à frais de livraison nuls : rien à verser, et surtout pas un
                    // message que la marketplace rejetterait (elle exige un montant
                    // strictement positif) et qui finirait en abandon.
                    _logger.LogWarning(
                        "Course {JobId} livrée avec une rémunération nulle (frais de livraison {Fee}) — "
                        + "aucune annonce de paiement.",
                        job.Id, job.ShippingFee);
                    return;
                }

                var payload = new CourierDeliveryCompletedPayload(
                    courierId.Value, job.OrderId, job.Id.Value, amount, job.Currency);

                await _outbound.AddAsync(
                    OutboundMessage.Create(
                        OutboundMessage.CourierDeliveryCompletedType,
                        JsonSerializer.Serialize(payload, JsonOptions),
                        now),
                    cancellationToken);

                _metrics.JobDelivered();

                _logger.LogInformation(
                    "Course {JobId} livrée : {Amount} {Currency} en file pour le coursier {CourierId}.",
                    job.Id, amount, job.Currency, courierId);
            });
    }

    /// <summary>
    /// La course échoue en cours de route : acheteur introuvable, colis refusé, ou plus
    /// rien à remettre. La course se ferme, le coursier redevient disponible, et
    /// l'échec part en file vers la marketplace.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// L'ÉCHEC S'ANNONCE, EXACTEMENT COMME LA LIVRAISON. IL NE S'ANNONÇAIT PAS.
    ///
    /// Cette méthode se contentait de poser Status = Failed et de libérer le coursier.
    /// AUCUN message n'était écrit — seule MarkDeliveredAsync en produisait un. La
    /// marketplace n'apprenait donc jamais l'échec : l'expédition y restait « en
    /// préparation » indéfiniment, la séquestre du vendeur n'était pas tranchée, la
    /// commande n'atteignait aucun état final. Et rien ne le signalait : pas d'erreur,
    /// pas de message abandonné à relever, pas de compteur — du point de vue du service
    /// coursier, la course s'était close proprement.
    ///
    /// Le message est écrit DANS LA MÊME TRANSACTION que le changement de statut, pour la
    /// raison exacte qui vaut déjà pour la livraison : une course close dont l'annonce
    /// s'est perdue ne laisse RIEN à réessayer. L'envoi, lui, revient au drain, autant de
    /// fois qu'il le faudra, et son abandon éventuel est visible.
    /// ═════════════════════════════════════════════════════════════════════════
    ///
    /// SANS EXIGENCE DE POSITION FRAÎCHE, délibérément : c'est l'issue de secours du
    /// coursier, et un GPS muet ne doit jamais l'empêcher de clore une course qu'il ne
    /// peut pas finir. Voir CourierActionPolicy, et la garde requirePosition plus bas.
    /// </summary>
    public async Task<Result> FailAsync(
        DeliveryJobId jobId, CourierId courierId, string reason, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        return await MutateAsync(
            jobId, courierId,
            job => job.Fail(reason, now),
            cancellationToken,
            release: true,
            beforeSave: async job =>
            {
                // LA RAISON EST RELUE SUR LA COURSE, pas reprise de l'argument : c'est
                // Fail qui la normalise — vide remplacé par un libellé par défaut, espaces
                // coupés. La marketplace doit lire ce que la course PORTE, pas ce que
                // l'appelant a tapé, sans quoi un motif vide traverserait la file.
                var payload = new CourierDeliveryFailedPayload(
                    courierId.Value,
                    job.OrderId,
                    job.Id.Value,
                    job.FailureReason ?? "Échec de livraison.",
                    now);

                await _outbound.AddAsync(
                    OutboundMessage.Create(
                        OutboundMessage.CourierDeliveryFailedType,
                        JsonSerializer.Serialize(payload, JsonOptions),
                        now),
                    cancellationToken);

                _metrics.JobFailed();

                // EN « WARNING », PAS EN « INFORMATION » : une course échouée laisse une
                // commande en attente d'une décision humaine ou d'une reprise. Ce n'est
                // pas le déroulement normal d'une journée.
                _logger.LogWarning(
                    "Course {JobId} (commande {OrderId}) DÉCLARÉE EN ÉCHEC par le coursier {CourierId} : "
                    + "{Reason}. Annonce mise en file pour la marketplace.",
                    job.Id, job.OrderId, courierId, job.FailureReason);
            });
    }

    /// <summary>
    /// La commande a été annulée côté marketplace : la course tombe avec elle.
    ///
    /// SANS APPELANT COURSIER — c'est la marketplace qui décide, pas le livreur. Le
    /// coursier assigné est libéré au passage, sinon il resterait « en course » pour une
    /// course qui n'existe plus et ne recevrait plus jamais d'offre.
    ///
    /// SILENCIEUX SI LA COURSE EST INCONNUE. Une commande annulée avant d'être prête n'a
    /// jamais produit de course : ce n'est pas une anomalie, et lever une erreur ferait
    /// rejouer indéfiniment un message parfaitement traité.
    /// </summary>
    public async Task<Result> CancelByOrderAsync(
        Guid orderId, string reason, CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetByOrderAsync(orderId, cancellationToken);
        if (job is null)
        {
            _logger.LogInformation(
                "Commande {OrderId} annulée : aucune course associée — rien à annuler.", orderId);
            return Result.Success();
        }

        var cancelled = job.Cancel(reason, _clock.GetUtcNow().UtcDateTime);
        if (cancelled.IsFailure)
        {
            // Une course déjà livrée ne s'annule plus. Ce n'est pas au message de
            // réessayer : c'est un cas à trancher par un humain.
            _logger.LogWarning(
                "Commande {OrderId} annulée mais course {JobId} NON annulable ({Code}) — statut {Status}.",
                orderId, job.Id, cancelled.Error.Code, job.Status);
            return cancelled;
        }

        await ReleaseCourierAsync(job.AssignedCourierId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <param name="beforeSave">
    /// Exécuté APRÈS la mutation et AVANT l'enregistrement — donc dans la même
    /// transaction. C'est par là que la livraison met son message de paiement en file, et
    /// que l'échec met le sien : dans les deux cas, l'état de la course et l'annonce qui
    /// en découle sont écrits par le même SaveChanges, ou ne sont écrits ni l'un ni
    /// l'autre.
    /// </param>
    private async Task<Result> MutateAsync(
        DeliveryJobId jobId,
        CourierId courierId,
        Func<DeliveryJob, Result> mutate,
        CancellationToken cancellationToken,
        bool release = false,
        bool requirePosition = false,
        Func<DeliveryJob, Task>? beforeSave = null)
    {
        var job = await _jobs.GetByIdAsync(jobId, cancellationToken);

        // ═════════════════════════════════════════════════════════════════════════
        // « INTROUVABLE » DANS LES DEUX CAS, ET J'AVAIS D'ABORD TRANCHÉ L'INVERSE.
        //
        // Ce passage répondait 404 `job.not_found` pour une course inexistante et 403
        // `job.not_yours` pour une course réelle assignée à un concurrent. Un coursier
        // parcourait les identifiants et cartographiait la table des courses — et le
        // volume de livraisons est une donnée concurrentielle.
        //
        // L'argument que j'avais retenu pour garder le 403 — « prise par un autre » est
        // l'information dont le coursier a besoin quand deux acceptent au même instant —
        // NE VAUT QUE POUR `Accept`. Ici on est dans `MutateAsync`, le passage des six
        // TRANSITIONS (ramassage, transit, livraison, incident, échec) : l'appelant n'a
        // par construction jamais eu la course, il n'y a pas de course simultanée à
        // expliquer, et il n'y a rien d'utile à lui dire.
        //
        // Ce qui rend le tout atteignable : `/api/courier` n'exige aucun rôle et les
        // cinq hôtes partagent une clé de signature. N'importe quel jeton acheteur entre
        // ici. La garde de propriété est donc la seule barrière, et elle ne doit pas
        // parler.
        // ═════════════════════════════════════════════════════════════════════════
        if (job is null || job.AssignedCourierId != courierId)
        {
            return Result.Failure(Error.NotFound("job.not_found", "Course introuvable."));
        }

        // POSITION FRAÎCHE pour les transitions qui supposent un DÉPLACEMENT ; pas pour
        // les issues de secours (incident de ramassage, échec de course), qu'un GPS
        // muet ne doit jamais empêcher. Voir CourierActionPolicy.
        if (requirePosition)
        {
            var courierNow = await _couriers.GetByIdAsync(courierId, cancellationToken);
            var allowed = CourierActionPolicy.Check(courierNow, _clock.GetUtcNow().UtcDateTime);
            if (allowed.IsFailure)
            {
                return allowed;
            }
        }

        var mutated = mutate(job);
        if (mutated.IsFailure)
        {
            return mutated;
        }

        if (release)
        {
            await ReleaseCourierAsync(courierId, cancellationToken);
        }

        if (beforeSave is not null)
        {
            await beforeSave(job);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task ReleaseCourierAsync(CourierId? courierId, CancellationToken cancellationToken)
    {
        if (courierId is not { } id)
        {
            return;
        }

        var courier = await _couriers.GetByIdAsync(id, cancellationToken);
        courier?.ReleaseFromDelivery();
    }
}
