using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Domain.Jobs;
using HbaExpress.Dispatch.Domain.Matching;
using Marketplace.BuildingBlocks.Domain.Results;
using Microsoft.Extensions.Logging;

namespace HbaExpress.Dispatch.Application.Jobs;

/// <summary>Résultat d'un passage de balayage.</summary>
public sealed record RedispatchOutcome(int Rewidened, int Expired);

/// <summary>
/// Reprend les courses que personne n'a acceptées : élargit, puis abandonne le dispatch
/// interne au bout du délai global.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// SANS CE BALAYAGE, UNE COURSE NON ACCEPTÉE RESTE OFFERTE POUR TOUJOURS.
///
/// Les offres expirent — mais expirer une offre ne fait rien arriver. La course garde
/// son statut, aucune nouvelle vague ne part, et rien ne signale l'anomalie. C'est la
/// forme de panne la plus coûteuse d'un dispatch : pas d'erreur, pas de journal, juste
/// un acheteur qui attend une livraison que personne ne viendra faire.
///
/// L'ÉLARGISSEMENT PROGRESSIF, PLUTÔT QU'UN GRAND RAYON D'EMBLÉE. Offrir large tout de
/// suite trouverait plus vite un preneur — mais un preneur à huit kilomètres, qui met
/// vingt minutes. On commence près, et on n'élargit que si personne ne répond : le coût
/// d'un tour de plus (vingt-cinq secondes) est bien inférieur à celui d'une course
/// acceptée par le mauvais coursier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RedispatchService
{
    /// <summary>
    /// Au-delà, le dispatch interne abandonne et la course bascule vers le repli en
    /// cascade (CDC §5.7). CINQ MINUTES : c'est long pour un acheteur qui attend, et
    /// c'est déjà douze vagues d'offres. Si personne n'a pris en cinq minutes, ce n'est
    /// plus un problème de rayon — c'est qu'il n'y a pas de coursier disponible.
    /// </summary>
    public static readonly TimeSpan GlobalDeadline = TimeSpan.FromMinutes(5);

    private readonly IDeliveryJobRepository _jobs;
    private readonly DeliveryJobService _dispatch;
    private readonly IDispatchUnitOfWork _unitOfWork;
    private readonly ILogger<RedispatchService> _logger;
    private readonly TimeProvider _clock;
    private readonly IDispatchMetrics _metrics;

    public RedispatchService(
        IDeliveryJobRepository jobs,
        DeliveryJobService dispatch,
        IDispatchUnitOfWork unitOfWork,
        ILogger<RedispatchService> logger,
        TimeProvider? clock = null,
        IDispatchMetrics? metrics = null)
    {
        _jobs = jobs;
        _dispatch = dispatch;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _metrics = metrics ?? NullDispatchMetrics.Instance;
    }

    /// <summary>
    /// Un passage complet, DANS UNE SEULE TRANSACTION.
    ///
    /// Ce n'est pas pour l'atomicité — c'est pour la DURÉE DES VERROUS. La réservation
    /// des courses pose un « FOR UPDATE SKIP LOCKED » qui ne tient que jusqu'au commit :
    /// sans cette transaction, les verrous tomberaient avant qu'on ait traité la première
    /// course, et une seconde instance reprendrait le même lot.
    /// </summary>
    public Task<RedispatchOutcome> SweepAsync(int take = 50, CancellationToken cancellationToken = default)
        => _unitOfWork.InTransactionAsync(ct => SweepCoreAsync(take, ct), cancellationToken);

    private async Task<RedispatchOutcome> SweepCoreAsync(int take, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var pending = await _jobs.ListAwaitingRedispatchAsync(now, take, cancellationToken);

        var rewidened = 0;
        var expired = 0;

        foreach (var job in pending)
        {
            if (now - job.CreatedAtUtc >= GlobalDeadline)
            {
                if (await ExpireAsync(job, now, cancellationToken))
                {
                    expired++;
                }

                continue;
            }

            // Le rayon suit le nombre de vagues déjà tentées : chaque tour double la
            // portée et sollicite davantage de monde.
            var policy = WidenFor(job.CurrentRound);
            var offered = await _dispatch.OfferAsync(job, policy, cancellationToken);

            if (offered.IsSuccess && offered.Value > 0)
            {
                rewidened++;
            }

            // Zéro coursier trouvé n'est pas un échec : la course reste éligible au
            // prochain passage, et un coursier peut se connecter entre-temps.
        }

        if (rewidened > 0 || expired > 0)
        {
            _logger.LogInformation(
                "Balayage du dispatch : {Rewidened} course(s) ré-offerte(s), {Expired} expirée(s).",
                rewidened, expired);
        }

        return new RedispatchOutcome(rewidened, expired);
    }

    /// <summary>
    /// Rayon de la vague suivante. <paramref name="roundsDone"/> vaut zéro pour une
    /// course jamais offerte — elle repart alors sur le rayon initial.
    /// </summary>
    public static DispatchPolicy WidenFor(int roundsDone)
    {
        var policy = DispatchPolicy.FirstRound;
        for (var i = 0; i < roundsDone; i++)
        {
            policy = policy.Widen();
        }

        return policy;
    }

    private async Task<bool> ExpireAsync(DeliveryJob job, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var result = job.Expire(nowUtc);
        if (result.IsFailure)
        {
            return false;
        }

        await _dispatch.PersistAsync(cancellationToken);

        // ═════════════════════════════════════════════════════════════════════════
        // NIVEAU « WARNING » ET NON « INFORMATION », DÉLIBÉRÉMENT.
        //
        // Une course expirée n'est pas un échec technique : c'est le passage à l'étape
        // suivante de la cascade (prestataire externe, puis transporteur classique).
        // Sauf que CETTE CASCADE N'EXISTE PAS ENCORE. Tant qu'elle manque, une course
        // expirée est une commande qui n'a plus personne pour la livrer, et cela doit
        // se voir dans les journaux — pas se fondre dans le bruit.
        //
        // À rétrograder en information le jour où le repli sera branché.
        // ═════════════════════════════════════════════════════════════════════════
        // Le compteur, en plus du journal : un avertissement se lit après coup, un
        // compteur se surveille. Tant que le repli en cascade n'existe pas, chaque
        // incrément est un acheteur sans livreur.
        _metrics.JobExpired();

        _logger.LogWarning(
            "Course {JobId} (commande {OrderId}) EXPIRÉE après {Minutes} min : aucun coursier interne. " +
            "Le repli en cascade n'étant pas encore branché, cette commande n'a plus de livreur — à traiter à la main.",
            job.Id, job.OrderId, (int)(nowUtc - job.CreatedAtUtc).TotalMinutes);

        return true;
    }
}
