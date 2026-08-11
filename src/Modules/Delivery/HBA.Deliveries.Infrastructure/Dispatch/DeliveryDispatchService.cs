using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Application.Dispatch;
using HBA.Deliveries.Application.Drivers.Commands;
using HBA.Deliveries.Domain.Deliveries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HBA.Deliveries.Infrastructure.Dispatch;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA BOUCLE QUI FAIT VIVRE LE DISPATCH.
///
/// Sans elle, une course est créée, passe en « recherche de livreur »… et y reste.
/// L'agrégat sait tout faire, mais il n'a pas d'horloge : personne ne vient lui
/// dire « propose », ni « ce livreur n'a pas répondu ».
///
/// DEUX TÂCHES, UN SEUL TOUR
///
///   1. EXPIRER les propositions sans réponse. Elle vient EN PREMIER, et ce n'est
///      pas un détail d'ordonnancement : une course bloquée sur un livreur muet
///      n'est pas « en recherche », donc l'étape 2 ne la verrait pas. Expirer
///      d'abord, c'est remettre ces courses dans le circuit du même tour.
///
///   2. PROPOSER les courses en attente, de la plus ancienne à la plus récente.
///
/// ⚠️ UNE SEULE INSTANCE À LA FOIS — MÊME CONTRAINTE QUE L'OUTBOX.
///
/// Deux processus qui tournent en parallèle liraient les mêmes courses et les
/// proposeraient à deux livreurs différents. Les deux accepteraient ; un seul
/// obtiendrait la course, l'autre se serait dérouté pour rien. Sur une flotte
/// d'indépendants, c'est le genre d'incident qui se raconte et qui coûte des
/// livreurs.
///
/// D'où le drapeau <c>DISPATCH_ENABLED</c>, aligné par défaut sur
/// <c>OUTBOX_ENABLED</c> : les hôtes qui ne drainent pas l'outbox — les quatre
/// BFF — ne dispatchent pas non plus. Aucune configuration de déploiement à
/// modifier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class DeliveryDispatchService : BackgroundService
{
    /// <summary>
    /// Délai laissé au livreur pour répondre.
    ///
    /// Quarante-cinq secondes : assez pour sortir le téléphone de sa poche, trop
    /// court pour que le client s'inquiète. En dessous, on expire des livreurs qui
    /// allaient accepter ; au-dessus, une course reste immobilisée sur quelqu'un
    /// qui ne répondra jamais.
    /// </summary>
    public static readonly TimeSpan OfferTimeout = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Cadence de la boucle. Cinq secondes : le retard maximal entre le moment où
    /// une course devient proposable et celui où elle est proposée.
    /// </summary>
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    /// <summary>Nombre de courses traitées par tour, dans chacune des deux tâches.</summary>
    private const int BatchSize = 25;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeliveryDispatchService> _logger;

    public DeliveryDispatchService(IServiceScopeFactory scopeFactory, ILogger<DeliveryDispatchService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Dispatch des livraisons démarré (cadence {Interval}s, expiration des propositions {Timeout}s).",
            PollingInterval.TotalSeconds, OfferTimeout.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // ─────────────────────────────────────────────────────────────
                // UN SEUL PROCESSUS TRAITE UN TOUR.
                //
                // Sans ce verrou, deux répliques de l'API liraient les MÊMES
                // courses en attente et les proposeraient chacune à un livreur :
                // deux téléphones sonnent, un seul livreur arrive. Rien dans
                // l'agrégat ne l'attrape — Delivery n'a pas de jeton de
                // concurrence.
                //
                // Le tour est SAUTÉ si le verrou est pris. Ce n'est pas une perte :
                // l'autre processus fait le travail, et le tour suivant arrive
                // dans cinq secondes.
                // ─────────────────────────────────────────────────────────────
                await using (var scope = _scopeFactory.CreateAsyncScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<Persistence.DeliveriesDbContext>();

                    await using var runner = await SingleRunnerLock.TryAcquireAsync(
                        dbContext, SingleRunnerLock.DispatchKey, stoppingToken);

                    if (!runner.Acquired)
                    {
                        await Task.Delay(PollingInterval, stoppingToken);
                        continue;
                    }

                    await RunTurnAsync(stoppingToken);
                }

                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // arrêt normal de l'hôte
            }
            catch (Exception ex)
            {
                // Une erreur de tour ne doit pas tuer la boucle : le dispatch est
                // le seul chemin par lequel une course trouve un livreur.
                _logger.LogError(ex, "Erreur pendant un tour de dispatch.");

                try
                {
                    await Task.Delay(PollingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Un tour complet, une fois le verrou obtenu.</summary>
    private async Task RunTurnAsync(CancellationToken stoppingToken)
    {
        await ExpireStaleOffersAsync(stoppingToken);

        // AVANT la proposition : une course programmée dont l'heure est venue doit
        // entrer en recherche dans le MÊME tour, pas au suivant. Cinq secondes de
        // latence importent peu ; l'ordre, si : l'inverse ajouterait un tour
        // complet à chaque course programmée.
        await OpenScheduledWindowsAsync(stoppingToken);

        await OfferPendingDeliveriesAsync(stoppingToken);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// OUVRE LA RECHERCHE DES COURSES PROGRAMMÉES DONT L'HEURE APPROCHE.
    ///
    /// Sans cette passe, une course programmée resterait « Pending » pour
    /// toujours : la création ne la met plus en recherche — c'est tout l'intérêt
    /// d'un créneau — et rien d'autre ne viendrait constater que l'heure est
    /// venue. Le client aurait choisi un créneau pour ne jamais être livré.
    ///
    /// La fenêtre s'ouvre AVANT l'heure promise, du délai d'anticipation : il
    /// faut encore trouver quelqu'un et rouler. Voir
    /// Delivery.ScheduledDispatchLeadTime.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private async Task OpenScheduledWindowsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDeliveryUnitOfWork>();

        var now = DateTime.UtcNow;
        var due = await repository.ListScheduledDueAsync(now, BatchSize, cancellationToken);

        if (due.Count == 0)
        {
            return;
        }

        var opened = 0;

        foreach (var delivery in due)
        {
            // On repasse l'instant à l'agrégat plutôt que de lui laisser lire
            // l'horloge : c'est le même « maintenant » que celui qui a servi à
            // la requête, donc pas de course entre les deux.
            var result = delivery.StartSearching(now);

            if (result.IsSuccess)
            {
                opened++;
            }
            else
            {
                // Ne devrait pas arriver — la requête filtre déjà sur l'état et
                // l'échéance. Si cela se produit, c'est que la requête et
                // l'agrégat ne sont plus d'accord, et il faut le savoir.
                _logger.LogWarning(
                    "Course programmée {DeliveryId} non ouverte : {Code}.",
                    delivery.Id.Value, result.Error.Code);
            }
        }

        if (opened > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Dispatch : {Count} course(s) programmée(s) entrée(s) en recherche.", opened);
        }
    }

    /// <summary>Rend au circuit les courses immobilisées sur un livreur silencieux.</summary>
    private async Task ExpireStaleOffersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var stale = await repository.ListStaleOffersAsync(OfferTimeout, BatchSize, cancellationToken);

        foreach (var delivery in stale)
        {
            // On relit la proposition en cours plutôt que de supposer : entre le
            // relevé et ce point, le livreur a pu accepter.
            var pending = delivery.Assignments
                .LastOrDefault(a => a.Outcome is AssignmentOutcome.Offered);

            if (pending is null)
            {
                continue;
            }

            var result = await sender.Send(
                new ExpireDeliveryOfferCommand(delivery.Id.Value, pending.DriverId.Value), cancellationToken);

            if (result.IsFailure)
            {
                // Attendu et sans gravité : le livreur a répondu entre-temps.
                _logger.LogDebug(
                    "Expiration ignorée pour la course {DeliveryId} : {Error}",
                    delivery.Id.Value, result.Error.Message);
            }
        }
    }

    /// <summary>Propose les courses en attente, de la plus ancienne à la plus récente.</summary>
    private async Task OfferPendingDeliveriesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var awaiting = await repository.ListAwaitingDriverAsync(BatchSize, cancellationToken);

        foreach (var delivery in awaiting)
        {
            // ─────────────────────────────────────────────────────────────────
            // UNE COURSE « SANS LIVREUR DISPONIBLE » EST RÉESSAYÉE, PAS OUBLIÉE.
            //
            // `ListAwaitingDriverAsync` inclut cet état à dessein. Mais l'agrégat
            // exige d'être en « recherche » pour accepter une proposition : on
            // rouvre donc la recherche avant de proposer. C'est ce qui permet à
            // une course créée dans une commune sans livreur de partir quand un
            // livreur s'y connecte, dix minutes plus tard.
            // ─────────────────────────────────────────────────────────────────
            if (delivery.Status is DeliveryStatus.NoDriverAvailable)
            {
                var reopened = delivery.StartSearching();
                if (reopened.IsFailure)
                {
                    continue;
                }

                var unitOfWork = scope.ServiceProvider
                    .GetRequiredService<Application.Abstractions.IDeliveryUnitOfWork>();
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var result = await sender.Send(new DispatchDeliveryCommand(delivery.Id.Value), cancellationToken);

            if (result.IsFailure)
            {
                _logger.LogDebug(
                    "Dispatch sans effet pour la course {DeliveryId} : {Error}",
                    delivery.Id.Value, result.Error.Message);
                continue;
            }

            if (result.Value.DriverId is null)
            {
                // Aucun livreur trouvé à ce tour. Ce n'est pas une erreur : la
                // course sera reproposée au tour suivant, avec un rayon élargi
                // au-delà de deux tentatives.
                _logger.LogDebug(
                    "Aucun livreur pour la course {DeliveryId} (rayon {Radius} km).",
                    delivery.Id.Value, result.Value.RadiusKm);
            }
        }
    }
}
