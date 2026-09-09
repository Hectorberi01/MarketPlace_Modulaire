using HbaExpress.Dispatch.Application.Jobs;

namespace HbaExpress.Dispatch.Api.BackgroundJobs;

/// <summary>
/// Fait tourner le balayage de redispatch.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// C'EST CE SERVICE QUI DONNE VIE À RedispatchService — SANS LUI, IL N'EST QUE DU CODE.
///
/// Le balayage rattrape les courses que personne n'a acceptées : il élargit le rayon,
/// puis abandonne au bout du délai global. Non appelé, il ne rattrape rien, et une course
/// non acceptée reste offerte pour toujours — sans erreur, sans journal, avec un acheteur
/// qui attend. La panne la plus coûteuse est celle qui ne se voit pas.
///
/// TOUTES LES DIX SECONDES, PAS TOUTES LES MINUTES. La fenêtre d'offre est de
/// vingt-cinq secondes : balayer plus lentement laisserait une course inerte plus
/// longtemps qu'elle n'a été offerte, et le délai global de cinq minutes se consommerait
/// en attente plutôt qu'en vagues d'offres.
///
/// PLUSIEURS INSTANCES SONT DÉSORMAIS SÛRES. La réservation des courses pose un
/// « SELECT … FOR UPDATE SKIP LOCKED » et le passage entier tient dans une transaction :
/// deux balayages concurrents se partagent les courses au lieu de les reprendre chacun.
/// Auparavant, ils lançaient deux vagues d'offres pour la même course, et le même
/// coursier la voyait deux fois. L'interrupteur ci-dessous reste utile pour concentrer le
/// balayage sur une instance.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RedispatchBackgroundService : BackgroundService
{
    /// <summary>Interrupteur : à mettre à faux sur les instances qui ne balaient pas.</summary>
    public const string EnabledKey = "Dispatch:Redispatch:Enabled";

    public const string IntervalKey = "Dispatch:Redispatch:IntervalSeconds";
    public const string BatchSizeKey = "Dispatch:Redispatch:BatchSize";

    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);
    private const int DefaultBatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RedispatchBackgroundService> _logger;

    public RedispatchBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<RedispatchBackgroundService> logger)
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
                "Balayage de redispatch DÉSACTIVÉ ({Key} = false) sur cette instance. " +
                "Si aucune autre ne le fait tourner, les courses non acceptées ne seront JAMAIS ré-offertes.",
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
            "Balayage de redispatch actif : toutes les {Interval}, par lots de {BatchSize}.",
            interval, batchSize);

        using var timer = new PeriodicTimer(interval);

        // L'ATTENTE EST DANS LE TRY, PAS SEULEMENT LE BALAYAGE. À l'arrêt de
        // l'application, WaitForNextTickAsync lève OperationCanceledException : laissée
        // remonter, elle ferait passer ce service pour un service EN PANNE, et l'hôte
        // s'arrêterait en signalant une erreur là où il n'y a qu'un arrêt normal.
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    // UN SCOPE PAR PASSAGE. Les services applicatifs et le DbContext sont
                    // « scoped » ; les capturer une fois pour toute la vie du processus
                    // ferait grossir indéfiniment un unique ChangeTracker, et ferait
                    // travailler tous les passages sur des entités déjà chargées.
                    using var scope = _scopeFactory.CreateScope();
                    var redispatch = scope.ServiceProvider.GetRequiredService<RedispatchService>();
                    await redispatch.SweepAsync(batchSize, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // ON NE LAISSE JAMAIS LA BOUCLE MOURIR. Une exception non rattrapée dans
                    // ExecuteAsync termine le BackgroundService en silence : l'application
                    // continue de répondre, les sondes de santé restent vertes, et plus
                    // aucune course n'est jamais ré-offerte. Une base momentanément
                    // injoignable ne doit pas coûter le balayage pour le reste de la vie du
                    // processus.
                    _logger.LogError(ex, "Passage de balayage du dispatch en échec — reprise au prochain tick.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Balayage de redispatch arrêté (arrêt de l'application).");
        }
    }
}
