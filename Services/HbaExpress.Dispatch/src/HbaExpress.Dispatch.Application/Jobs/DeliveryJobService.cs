using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using HbaExpress.Dispatch.Domain.Matching;
using Marketplace.BuildingBlocks.Domain.Results;
using Microsoft.Extensions.Logging;

namespace HbaExpress.Dispatch.Application.Jobs;

/// <summary>
/// Crée les courses, les offre, et enregistre qui les prend.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE SERVICE NE DÉCIDE PAS QUI GAGNE — LA BASE LE FAIT.
///
/// Toute la logique d'éligibilité est dans CourierMatcher (fonction pure) et toute la
/// règle d'acceptation dans DeliveryJob (agrégat). Ce service ne fait que les enchaîner
/// et persister. C'est volontaire : il n'y a rien ici qu'on ait besoin de tester avec
/// une base, sauf la concurrence — qui l'est déjà, à part.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeliveryJobService
{
    private readonly IDeliveryJobRepository _jobs;
    private readonly ICourierRepository _couriers;
    private readonly IOfferNotifier _notifier;
    private readonly IDispatchUnitOfWork _unitOfWork;
    private readonly ILogger<DeliveryJobService> _logger;
    private readonly TimeProvider _clock;

    /// <summary>Délai laissé au coursier pour répondre à une offre.</summary>
    public static readonly TimeSpan OfferWindow = TimeSpan.FromSeconds(25);

    public DeliveryJobService(
        IDeliveryJobRepository jobs,
        ICourierRepository couriers,
        IOfferNotifier notifier,
        IDispatchUnitOfWork unitOfWork,
        ILogger<DeliveryJobService> logger,
        TimeProvider? clock = null)
    {
        _jobs = jobs;
        _couriers = couriers;
        _notifier = notifier;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Crée la course annoncée par la marketplace, puis lance la première vague d'offres.
    ///
    /// IDEMPOTENT SUR LA COMMANDE. L'événement « commande prête » peut arriver deux fois :
    /// deux vendeurs qui terminent à la même seconde travaillent dans deux transactions
    /// distinctes, et chacune peut conclure que tout est prêt. Une deuxième arrivée ne
    /// crée pas de seconde course — elle ne fait rien du tout, ce qui est exactement ce
    /// qu'on veut d'un message rejoué.
    /// </summary>
    public async Task<Result<DeliveryJobId>> CreateFromBriefAsync(
        DeliveryBrief brief, CancellationToken cancellationToken = default)
    {
        var existing = await _jobs.GetByOrderAsync(brief.OrderId, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Commande {OrderId} : course déjà créée ({JobId}) — annonce rejouée, ignorée.",
                brief.OrderId, existing.Id);
            return existing.Id;
        }

        var created = DeliveryJob.Create(
            brief.OrderId, brief.ShippingFee, brief.Currency,
            brief.Pickups.Select(ToDraft),
            ToDraft(brief.Dropoff));

        if (created.IsFailure)
        {
            // Une course refusée ici — typiquement un ramassage sans point de repère —
            // n'est pas un incident du dispatch : c'est une commande que la marketplace
            // n'aurait pas dû laisser passer. On le dit fort, sinon la commande reste
            // silencieusement sans livreur.
            _logger.LogError(
                "Commande {OrderId} : course IMPOSSIBLE à créer ({Code}) — {Message}",
                brief.OrderId, created.Error.Code, created.Error.Message);
            return Result.Failure<DeliveryJobId>(created.Error);
        }

        var job = created.Value;
        await _jobs.AddAsync(job, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // La première vague part APRÈS que la course soit persistée. Si le dispatch
        // échoue, la course existe quand même et le balayage de redispatch la rattrapera.
        // L'inverse — offrir avant d'enregistrer — proposerait une course qui pourrait
        // n'avoir jamais existé.
        await OfferAsync(job, DispatchPolicy.FirstRound, cancellationToken);
        return job.Id;
    }

    /// <summary>
    /// Diffuse la course à une vague de coursiers éligibles.
    ///
    /// Les coursiers DÉJÀ SOLLICITÉS sur cette course sont exclus : les rappeler à
    /// chaque élargissement du rayon ferait sonner cinq fois le téléphone de celui qui
    /// n'a pas répondu la première fois, et abîmerait son taux d'acceptation pour une
    /// seule course.
    /// </summary>
    public async Task<Result<int>> OfferAsync(
        DeliveryJob job, DispatchPolicy policy, CancellationToken cancellationToken = default)
    {
        var origin = job.FirstPickup;
        if (origin is null)
        {
            return Result.Failure<int>(Error.Validation("job.no_pickup", "Course sans point de ramassage."));
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var alreadyAsked = job.Offers.Select(o => o.CourierId).ToHashSet();

        var candidates = await _couriers.ListCandidatesAsync(origin.CommuneCode, cancellationToken);
        var matched = CourierMatcher.Match(job, candidates, policy, now, alreadyAsked);

        if (matched.Count == 0)
        {
            _logger.LogWarning(
                "Course {JobId} (commande {OrderId}) : AUCUN coursier éligible dans {Radius} km de {Commune}.",
                job.Id, job.OrderId, policy.RadiusKm, origin.CommuneCode);
            return 0;
        }

        var offered = job.Offer(matched.Select(m => m.CourierId), now, OfferWindow);
        if (offered.IsFailure)
        {
            return Result.Failure<int>(offered.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // LA NOTIFICATION VIENT APRÈS LA PERSISTANCE, et son échec est avalé.
        // Une offre poussée mais non enregistrée serait invisible du serveur : le
        // coursier l'accepterait et s'entendrait répondre qu'elle n'existe pas. L'inverse
        // — enregistrée mais non poussée — est bénin : il la verra en ouvrant l'app.
        foreach (var match in matched)
        {
            try
            {
                await _notifier.NotifyOfferAsync(match.CourierId, job, now + OfferWindow, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex, "Course {JobId} : offre non poussée au coursier {CourierId} — elle reste valide en base.",
                    job.Id, match.CourierId);
            }
        }

        _logger.LogInformation(
            "Course {JobId} : offerte à {Count} coursier(s), vague {Round}, rayon {Radius} km.",
            job.Id, matched.Count, job.CurrentRound, policy.RadiusKm);

        return matched.Count;
    }

    /// <summary>
    /// Un coursier prend la course. Le perdant d'une course simultanée reçoit une erreur
    /// métier, pas une exception technique — voir DispatchConcurrency côté infrastructure.
    /// </summary>
    public async Task<Result> AcceptAsync(
        DeliveryJobId jobId, CourierId courierId, CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return Result.Failure(Error.NotFound("job.not_found", "Course introuvable."));
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var losers = job.Offers
            .Where(o => o.CourierId != courierId && o.Status == DispatchOfferStatus.Pending)
            .Select(o => o.CourierId)
            .ToList();

        var accepted = job.Accept(courierId, now);
        if (accepted.IsFailure)
        {
            return accepted;
        }

        var courier = await _couriers.GetByIdAsync(courierId, cancellationToken);
        courier?.MarkOnDelivery();

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Retirer l'offre des autres téléphones tout de suite : sans cela, ils voient une
        // course qui n'existe plus et essaient de l'accepter pour rien.
        foreach (var loser in losers)
        {
            try
            {
                await _notifier.NotifyOfferWithdrawnAsync(loser, job.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Course {JobId} : retrait d'offre non poussé au coursier {CourierId}.", job.Id, loser);
            }
        }

        return Result.Success();
    }

    public async Task<Result> DeclineAsync(
        DeliveryJobId jobId, CourierId courierId, CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return Result.Failure(Error.NotFound("job.not_found", "Course introuvable."));
        }

        var declined = job.Decline(courierId, _clock.GetUtcNow().UtcDateTime);
        if (declined.IsFailure)
        {
            return declined;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Persiste les modifications en cours. Exposé pour le balayage de redispatch, qui
    /// mute une course (expiration) sans passer par les méthodes ci-dessus.
    /// </summary>
    public Task PersistAsync(CancellationToken cancellationToken = default)
        => _unitOfWork.SaveChangesAsync(cancellationToken);

    private static PickupDraft ToDraft(PickupBrief brief)
        => new(
            brief.ShipmentId, brief.SellerId, brief.SellerName, brief.SellerPhone,
            brief.CommuneCode, brief.Quartier, brief.Landmark, brief.Line,
            ToPoint(brief.Latitude, brief.Longitude),
            brief.Parcels.Select(p => (p.Sku, p.Quantity)).ToList());

    private static DropoffDraft ToDraft(DropoffBrief brief)
        => new(
            brief.BuyerId, brief.Recipient, brief.Phone,
            brief.CommuneCode, brief.Quartier, brief.Landmark, brief.Line,
            ToPoint(brief.Latitude, brief.Longitude));

    /// <summary>
    /// Coordonnées facultatives. Une position invalide — (0, 0) d'un GPS sans fix, par
    /// exemple — est traitée comme ABSENTE plutôt que de faire échouer la course : le
    /// dispatch sait travailler sans, en s'appuyant sur la commune.
    /// </summary>
    private static GeoPoint? ToPoint(double? latitude, double? longitude)
    {
        if (latitude is not { } lat || longitude is not { } lon)
        {
            return null;
        }

        var point = GeoPoint.Create(lat, lon);
        return point.IsSuccess ? point.Value : null;
    }
}
