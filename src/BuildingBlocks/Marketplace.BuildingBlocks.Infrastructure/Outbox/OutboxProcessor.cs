using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.Application.Observability;
using Marketplace.BuildingBlocks.Infrastructure.Events;
using Marketplace.BuildingBlocks.Infrastructure.Serialization;
using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Processeur d'outbox d'un module : lit les messages éligibles, les publie (dispatch
/// in-process) et les marque traités. Générique sur le DbContext du module pour rester
/// une seule implémentation réutilisée partout.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE PROCESSEUR REJOUAIT LES MESSAGES EMPOISONNÉS À L'INFINI. TOUTES LES 5 SECONDES.
///
/// L'ancienne version faisait exactement ceci en cas d'échec :
///
///     message.Error = ex.Message;                     // on note l'erreur
///     _logger.LogError(ex, "Échec de publication…");  // on la journalise
///     // … et c'est tout. ProcessedOnUtc reste null.
///
/// Le message revenait donc au tour suivant. Et au suivant. Sans limite, sans délai,
/// sans issue. Une adresse e-mail invalide, un type d'événement renommé, un JSON devenu
/// illisible — et la boucle tournait jusqu'à la fin des temps.
///
/// Le vrai danger n'était pas le bruit, mais le BLOCAGE DE TÊTE DE FILE : le lot est
/// trié par date et plafonné à 50. Chaque message mort confisquait une place, pour
/// toujours. À 50, l'outbox du module se figeait — plus une seule commande confirmée,
/// plus un seul vendeur crédité, plus une seule notification. Une panne qui commence par
/// un e-mail refusé, et finit par une plateforme muette.
///
/// Trois mécanismes ferment cela :
///   1. BACKOFF  — un message en échec sort du lot le temps de sa temporisation ; les
///                 messages sains passent devant. Le blocage de tête de file disparaît.
///   2. PLAFOND  — au bout de MaxAttempts (~2 h), le message part en LETTRE MORTE : il
///                 cesse de consommer une place, et devient VISIBLE.
///   3. ALERTE   — la mise en lettre morte est journalisée en Critical. C'est une perte
///                 métier, pas un incident technique : elle doit réveiller quelqu'un.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// ⚠️ <b>UNE SEULE INSTANCE À LA FOIS.</b> La lecture n'est pas protégée par un
/// <c>SELECT … FOR UPDATE SKIP LOCKED</c> : deux processeurs concurrents liraient les mêmes
/// messages et les dispatcheraient DEUX FOIS. C'est pourquoi les 4 BFF posent
/// <c>OUTBOX_ENABLED=false</c> et que seule l'API draine.
/// <b>Ne pas mettre l'API à l'échelle horizontale sans implémenter le verrou de ligne
/// d'abord</b> — sans quoi chaque gain vendeur serait crédité autant de fois qu'il y a de
/// répliques.
/// </para>
/// </summary>
public sealed class OutboxProcessor<TDbContext> : BackgroundService
    where TDbContext : DbContext, IOutboxDbContext
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor<TDbContext>> _logger;
    private readonly OutboxRetryPolicy _retryPolicy;
    private readonly IOutboxMetrics _metrics;
    private readonly Random _random = new();

    /// <summary>Nom du module, déduit du DbContext : « SettlementDbContext » → « Settlement ».</summary>
    private static readonly string ModuleName = typeof(TDbContext).Name.Replace("DbContext", string.Empty);
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessor<TDbContext>> logger,
        OutboxRetryPolicy retryPolicy,
        IOutboxMetrics metrics)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _retryPolicy = retryPolicy;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Arrêt normal de l'hôte : on sort proprement sans faire planter le host.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du traitement de l'outbox de {Context}", typeof(TDbContext).Name);

                // Pause avant nouvel essai, elle aussi protégée contre l'annulation.
                try
                {
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IntegrationEventDispatcher>();

        var nowUtc = DateTime.UtcNow;

        // Sont ÉLIGIBLES les messages non traités, non enterrés, et dont la temporisation
        // est écoulée.
        //
        // C'est le filtre `NextAttemptAtUtc` qui supprime le blocage de tête de file : un
        // message en échec disparaît du lot jusqu'à son heure, laissant la place aux
        // messages sains. Auparavant, il squattait sa place indéfiniment.
        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null
                        && m.DeadLetteredOnUtc == null
                        && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= nowUtc))
            .OrderBy(m => m.OccurredOnUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            try
            {
                var type = EventTypeName.Resolve(message.Type);
                if (JsonSerializer.Deserialize(message.Content, type, SerializerOptions) is not IntegrationEvent integrationEvent)
                {
                    throw new InvalidOperationException($"Désérialisation impossible pour {message.Type}");
                }

                await dispatcher.DispatchAsync(integrationEvent, cancellationToken);

                message.ProcessedOnUtc = DateTime.UtcNow;
                message.Error = null;
                message.NextAttemptAtUtc = null;
            }
            catch (Exception ex)
            {
                HandleFailure(message, ex);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void HandleFailure(OutboxMessage message, Exception exception)
    {
        // La DÉCISION (temporiser ou enterrer) appartient à la politique — testable, et
        // testée. Le processeur ne fait plus qu'appliquer et journaliser.
        var deadLettered = _retryPolicy.RegisterFailure(message, exception.Message, _random, DateTime.UtcNow);

        if (deadLettered)
        {
            // CRITICAL, et pas Error. Ce n'est plus « une tentative a échoué » — c'est
            // « cet événement métier ne sera JAMAIS traité ». Un e-mail de réinitialisation
            // qui ne partira pas, un gain vendeur qui ne sera pas crédité, un stock qui ne
            // sera pas libéré. Quelqu'un doit le voir, corriger la cause, et rejouer :
            // GET /admin/outbox/dead-letters.
            _logger.LogCritical(
                exception,
                "LETTRE MORTE — outbox {Context}, message {MessageId} de type {Type} abandonné après {Attempts} tentatives. "
                + "CET ÉVÉNEMENT NE SERA JAMAIS TRAITÉ. Corriger la cause, puis rejouer via /admin/outbox/dead-letters.",
                typeof(TDbContext).Name, message.Id, message.Type, message.AttemptCount);

            // LA métrique qui doit rester à zéro. Une alerte y est adossée
            // (OutboxDeadLetter dans prometheus-rules.yml) : sans elle, on aurait
            // simplement échangé une boucle bruyante contre un échec invisible.
            _metrics.DeadLettered(ModuleName, message.Type);

            return;
        }

        // Warning, pas Error : un échec isolé est ATTENDU (le réseau tombe, un fournisseur
        // hoquette, la base redémarre). Ce qui mérite une alerte, c'est la lettre morte
        // ci-dessus. Journaliser chaque tentative en Error noierait le seul signal qui compte.
        _metrics.PublishFailed(ModuleName, message.Type);

        _logger.LogWarning(
            exception,
            "Échec de publication du message {MessageId} ({Type}), tentative {Attempts}/{Max}. Prochain essai à {NextAttempt:u}.",
            message.Id, message.Type, message.AttemptCount, _retryPolicy.MaxAttempts, message.NextAttemptAtUtc);
    }
}
