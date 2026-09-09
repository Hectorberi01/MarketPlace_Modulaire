using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Domain.Outbound;

namespace HbaExpress.Dispatch.Api.BackgroundJobs;

/// <summary>
/// Vide la file sortante vers la marketplace.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// SANS CE DRAIN, LA FILE SE REMPLIT ET PERSONNE N'EST PAYÉ.
///
/// Le message « course livrée » est écrit dans la même transaction que la fermeture de
/// la course — c'est ce qui garantit qu'il existe. Encore faut-il qu'il parte. Une file
/// persistée sans drain, c'est une promesse de paiement qui s'accumule en base.
///
/// TROIS COMPORTEMENTS, ET AUCUN N'EST DÉCORATIF :
///
///  • RÉESSAI À DÉLAI CROISSANT — une marketplace momentanément injoignable ne doit pas
///    coûter le paiement, ni recevoir un martèlement au pire moment.
///  • ABANDON APRÈS PLAFOND — un message qui ne passera jamais (montant refusé par le
///    plafond, coursier inconnu, signature invalide) ne doit pas bloquer la file
///    derrière lui. Il est marqué abandonné, JAMAIS supprimé : une course livrée non
///    payée reste lisible en base.
///  • JOURNAL EN « ERREUR » À L'ABANDON — c'est de l'argent dû qui n'est pas parti.
///    C'est le message sur lequel brancher une alerte, au même titre que les lettres
///    mortes de l'outbox du monolithe.
///
/// PLUSIEURS EXÉCUTANTS SONT TOLÉRÉS ICI, CONTRAIREMENT AUX DEUX AUTRES BALAYAGES.
/// La requête ne pose délibérément pas de verrou de ligne : deux drains enverraient les
/// mêmes messages, et la marketplace ne paie qu'une fois par identifiant de course. Le
/// verrou coûterait une transaction ouverte pendant les appels HTTP du lot — voir
/// IOutboundMessageRepository.ListPendingAsync, où le raisonnement est écrit en entier.
/// L'interrupteur ci-dessous reste utile pour concentrer les envois sur une instance.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class OutboundDrainService : BackgroundService
{
    public const string EnabledKey = "Dispatch:Outbound:Enabled";
    public const string IntervalKey = "Dispatch:Outbound:IntervalSeconds";
    public const string BatchSizeKey = "Dispatch:Outbound:BatchSize";

    /// <summary>Nombre d'essais avant abandon. Avec le délai croissant, cela couvre environ deux heures.</summary>
    public const int MaxAttempts = 10;

    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(15);
    private const int DefaultBatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutboundDrainService> _logger;

    public OutboundDrainService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OutboundDrainService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue(EnabledKey, defaultValue: true))
        {
            _logger.LogWarning(
                "Drain sortant DÉSACTIVÉ ({Key} = false) sur cette instance. Si aucune autre ne le fait "
                + "tourner, AUCUNE course livrée ne sera payée.",
                EnabledKey);
            return;
        }

        var interval = TimeSpan.FromSeconds(
            _configuration.GetValue(IntervalKey, defaultValue: DefaultInterval.TotalSeconds));

        if (interval <= TimeSpan.Zero)
        {
            interval = DefaultInterval;
        }

        var batchSize = _configuration.GetValue(BatchSizeKey, defaultValue: DefaultBatchSize);

        _logger.LogInformation(
            "Drain sortant actif : toutes les {Interval}, par lots de {BatchSize}.", interval, batchSize);

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await DrainAsync(batchSize, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // La boucle ne meurt jamais : une base momentanément injoignable ne
                    // doit pas coûter le drain pour le reste de la vie du processus.
                    _logger.LogError(ex, "Passage de drain sortant en échec — reprise au prochain tick.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Drain sortant arrêté (arrêt de l'application).");
        }
    }

    private async Task DrainAsync(int batchSize, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var messages = scope.ServiceProvider.GetRequiredService<IOutboundMessageRepository>();
        var marketplace = scope.ServiceProvider.GetRequiredService<IMarketplaceClient>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDispatchUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var metrics = scope.ServiceProvider.GetRequiredService<IDispatchMetrics>();

        var now = clock.GetUtcNow().UtcDateTime;
        var pending = await messages.ListPendingAsync(now, batchSize, cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var message in pending)
        {
            try
            {
                await SendAsync(marketplace, message, cancellationToken);
                message.MarkSent(clock.GetUtcNow().UtcDateTime);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed(ex.Message, clock.GetUtcNow().UtcDateTime, MaxAttempts, BaseRetryDelay, MaxRetryDelay);
                metrics.OutboundSendFailed(message.Type);

                if (message.DeadLetteredAtUtc is not null)
                {
                    // LE COMPTEUR AVANT LE JOURNAL. C'est lui qui porte l'alerte
                    // DispatchOutboundDeadLetter : le journal explique, le compteur
                    // réveille.
                    metrics.OutboundDeadLettered(message.Type);

                    _logger.LogError(
                        ex,
                        "MESSAGE SORTANT ABANDONNÉ après {Attempts} tentatives — type {Type}, message {MessageId}. "
                        + "S'il s'agit d'une course livrée, LA RÉMUNÉRATION N'A PAS ÉTÉ VERSÉE : à traiter à la main.",
                        message.AttemptCount, message.Type, message.Id);
                }
                else
                {
                    _logger.LogWarning(
                        "Envoi du message {MessageId} ({Type}) en échec, tentative {Attempts}/{Max}. "
                        + "Prochain essai à {NextAttempt:u}.",
                        message.Id, message.Type, message.AttemptCount, MaxAttempts, message.NextAttemptAtUtc);
                }
            }
        }

        // UN SEUL ENREGISTREMENT POUR LE LOT. Chaque message porte déjà son propre
        // résultat ; les écrire un par un multiplierait les allers-retours sans rien
        // rendre plus sûr — un message renvoyé après une panne à mi-lot ne coûte qu'un
        // doublon, que la marketplace ignore.
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static Task SendAsync(
        IMarketplaceClient marketplace, OutboundMessage message, CancellationToken cancellationToken)
        => message.Type switch
        {
            OutboundMessage.CourierDeliveryCompletedType =>
                marketplace.SendCourierDeliveryCompletedAsync(message.Payload, cancellationToken),

            // L'ÉCHEC PASSE PAR LA MÊME FILE QUE LA LIVRAISON, et il le fallait : c'est
            // lui qui débloque une expédition restée « en préparation ». Oublier cette
            // branche laisserait le message s'écrire en base et finir en abandon sur un
            // « type inconnu » — la commande resterait figée exactement comme avant.
            OutboundMessage.CourierDeliveryFailedType =>
                marketplace.SendCourierDeliveryFailedAsync(message.Payload, cancellationToken),

            // UN TYPE INCONNU LÈVE, il n'est pas ignoré. Un message écrit par une version
            // du service et drainé par une autre qui ne le reconnaît plus doit finir en
            // abandon visible, pas disparaître dans un « default: return ».
            _ => throw new InvalidOperationException(
                $"Type de message sortant inconnu : « {message.Type} ». Aucun envoi possible.")
        };
}
