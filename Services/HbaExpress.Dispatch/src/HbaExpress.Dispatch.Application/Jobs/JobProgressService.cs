using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using Marketplace.BuildingBlocks.Domain.Results;
using Microsoft.Extensions.Logging;

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
    private readonly IDeliveryJobRepository _jobs;
    private readonly ICourierRepository _couriers;
    private readonly IDispatchUnitOfWork _unitOfWork;
    private readonly ILogger<JobProgressService> _logger;
    private readonly TimeProvider _clock;

    public JobProgressService(
        IDeliveryJobRepository jobs,
        ICourierRepository couriers,
        IDispatchUnitOfWork unitOfWork,
        ILogger<JobProgressService> logger,
        TimeProvider? clock = null)
    {
        _jobs = jobs;
        _couriers = couriers;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public Task<Result> StartPickupAsync(
        DeliveryJobId jobId, CourierId courierId, CancellationToken cancellationToken = default)
        => MutateAsync(jobId, courierId, job => job.StartPickup(), cancellationToken);

    public Task<Result> MarkPickedUpAsync(
        DeliveryJobId jobId, CourierId courierId, Guid pickupPointId, CancellationToken cancellationToken = default)
        => MutateAsync(
            jobId, courierId,
            job => job.MarkPickedUp(pickupPointId, _clock.GetUtcNow().UtcDateTime),
            cancellationToken);

    public Task<Result> ReportPickupIncidentAsync(
        DeliveryJobId jobId, CourierId courierId, Guid pickupPointId, string reason,
        CancellationToken cancellationToken = default)
        => MutateAsync(jobId, courierId, job => job.ReportPickupIncident(pickupPointId, reason), cancellationToken);

    public Task<Result> StartTransitAsync(
        DeliveryJobId jobId, CourierId courierId, CancellationToken cancellationToken = default)
        => MutateAsync(jobId, courierId, job => job.StartTransit(), cancellationToken);

    /// <summary>
    /// Course livrée : la course se ferme et le coursier redevient disponible.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE COURSIER N'EST PAS ENCORE PAYÉ PAR CE CHEMIN. C'EST LE TROU CONNU.
    ///
    /// La marketplace expose POST /api/delivery/courses/completed, signé et idempotent
    /// sur l'identifiant de course : c'est lui qui crédite le portefeuille du coursier.
    /// Ce service ne l'appelle pas encore.
    ///
    /// L'appel ne peut pas être un simple POST en ligne : si le réseau tombe entre la
    /// fermeture de la course et l'annonce, la course est livrée et personne n'est payé,
    /// sans que rien ne le signale. Il lui faut une file persistée, drainée jusqu'à
    /// acquittement — l'outbox du monolithe, en plus petit.
    ///
    /// D'ici là, ce niveau AVERTISSEMENT est le seul filet : une course livrée est une
    /// somme due qui n'a pas été versée, et cela doit se voir dans les journaux plutôt
    /// que se découvrir par la réclamation d'un coursier.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<Result> MarkDeliveredAsync(
        DeliveryJobId jobId, CourierId courierId, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var result = await MutateAsync(jobId, courierId, job => job.MarkDelivered(now), cancellationToken, release: true);

        if (result.IsSuccess)
        {
            _logger.LogWarning(
                "Course {JobId} LIVRÉE par le coursier {CourierId} : rémunération NON VERSÉE. " +
                "L'annonce à la marketplace (POST /api/delivery/courses/completed) n'est pas encore branchée — " +
                "à régler à la main tant que la file sortante n'existe pas.",
                jobId, courierId);
        }

        return result;
    }

    public Task<Result> FailAsync(
        DeliveryJobId jobId, CourierId courierId, string reason, CancellationToken cancellationToken = default)
        => MutateAsync(
            jobId, courierId,
            job => job.Fail(reason, _clock.GetUtcNow().UtcDateTime),
            cancellationToken,
            release: true);

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

    private async Task<Result> MutateAsync(
        DeliveryJobId jobId,
        CourierId courierId,
        Func<DeliveryJob, Result> mutate,
        CancellationToken cancellationToken,
        bool release = false)
    {
        var job = await _jobs.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return Result.Failure(Error.NotFound("job.not_found", "Course introuvable."));
        }

        if (job.AssignedCourierId != courierId)
        {
            return Result.Failure(Error.Forbidden(
                "job.not_yours", "Cette course est assignée à un autre coursier."));
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
