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
/// <b>PLUSIEURS INSTANCES SONT DÉSORMAIS SÛRES.</b> Le lot est réservé par un
/// <c>SELECT … FOR UPDATE SKIP LOCKED</c> : chaque processeur verrouille les lignes qu'il
/// prend, et les concurrents SAUTENT ces lignes au lieu de les attendre. Deux répliques se
/// partagent donc le travail au lieu de le dupliquer.
/// </para>
///
/// <para>
/// Auparavant, la lecture était un <c>SELECT</c> ordinaire : deux instances lisaient les
/// mêmes messages et les dispatchaient DEUX FOIS — chaque gain vendeur crédité autant de
/// fois qu'il y avait de répliques. C'est ce qui interdisait de mettre l'API à l'échelle,
/// et pourquoi les 4 BFF posent encore <c>OUTBOX_ENABLED=false</c>. Cette répartition
/// reste saine (les BFF n'ont rien à drainer), mais elle n'est plus une nécessité.
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

        // ═════════════════════════════════════════════════════════════════════════
        // LA TRANSACTION EXPLICITE N'EST PAS UN DÉTAIL : SANS ELLE, LE VERROU NE SERT
        // À RIEN.
        //
        // `FOR UPDATE` tient les lignes jusqu'à la fin de la TRANSACTION. Sans
        // transaction ouverte ici, EF en ouvrirait une implicite le temps du SELECT et
        // la validerait aussitôt : les verrous tomberaient avant même qu'on ait lu la
        // première ligne, et une seconde instance reprendrait exactement le même lot.
        //
        // Le piège est qu'un tel code A L'AIR correct — le SQL contient bien
        // « FOR UPDATE SKIP LOCKED » — et ne protège rien. Ne pas retirer ce bloc.
        // ═════════════════════════════════════════════════════════════════════════
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var messages = await ClaimBatchAsync(dbContext, nowUtc, cancellationToken);

        if (messages.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
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
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Réserve un lot de messages : lecture VERROUILLÉE, en sautant ce que d'autres
    /// traitent déjà.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// POURQUOI DU SQL BRUT ALORS QUE TOUT LE RESTE EST EN LINQ.
    ///
    /// `FOR UPDATE SKIP LOCKED` n'a pas d'équivalent LINQ, et c'est la seule construction
    /// qui répond exactement à la question posée : « donne-moi des lignes que personne
    /// d'autre n'a prises, et ne m'attends pas ». Sans SKIP LOCKED, un second processeur
    /// BLOQUERAIT sur les lignes du premier au lieu de passer à côté — on aurait échangé
    /// le double traitement contre une file d'attente.
    ///
    /// Le nom de table et de schéma sont lus dans le MODÈLE EF, pas écrits en dur :
    /// chaque module a son propre schéma, et une chaîne codée en dur ici viserait la
    /// mauvaise table pour vingt-cinq modules sur vingt-six.
    ///
    /// La requête n'est PAS composée après coup (pas de .Where ni de .Take derrière) :
    /// EF envelopperait alors le SQL dans une sous-requête, où PostgreSQL refuse
    /// « FOR UPDATE ». L'ordre, la limite et le verrou sont donc dans la chaîne elle-même.
    /// ═════════════════════════════════════════════════════════════════════════════
    ///
    /// Sont ÉLIGIBLES les messages non traités, non enterrés, et dont la temporisation
    /// est écoulée. C'est le filtre `NextAttemptAtUtc` qui supprime le blocage de tête de
    /// file : un message en échec disparaît du lot jusqu'à son heure, laissant la place
    /// aux messages sains.
    /// </summary>
    private static async Task<List<OutboxMessage>> ClaimBatchAsync(
        TDbContext dbContext, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(OutboxMessage))
            ?? throw new InvalidOperationException(
                $"La table d'outbox n'est pas dans le modèle de {typeof(TDbContext).Name}.");

        var schema = entityType.GetSchema();
        var table = entityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"La table d'outbox de {typeof(TDbContext).Name} n'a pas de nom.");

        var qualified = string.IsNullOrEmpty(schema) ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";

        // BatchSize est une constante entière du code : son interpolation ne peut porter
        // aucune injection. La date, elle, passe en PARAMÈTRE — c'est la seule valeur qui
        // vienne d'ailleurs que d'un littéral.
        // DEUX SIGNES DOLLAR, ET C'EST NÉCESSAIRE. Dans une chaîne brute interpolée, le
        // nombre de `$` fixe combien d'accolades ouvrent un trou — doubler les accolades
        // n'échappe rien, contrairement aux chaînes ordinaires. Avec `$$`, `{{…}}`
        // interpole et `{0}` reste littéral : c'est exactement ce qu'il faut, puisque
        // `{0}` est le marqueur de paramètre attendu par FromSqlRaw.
        var sql =
            $$"""
              SELECT * FROM {{qualified}}
              WHERE "ProcessedOnUtc" IS NULL
                AND "DeadLetteredOnUtc" IS NULL
                AND ("NextAttemptAtUtc" IS NULL OR "NextAttemptAtUtc" <= {0})
              ORDER BY "OccurredOnUtc"
              LIMIT {{BatchSize}}
              FOR UPDATE SKIP LOCKED
              """;

        return await dbContext.OutboxMessages
            .FromSqlRaw(sql, nowUtc)
            .ToListAsync(cancellationToken);
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
