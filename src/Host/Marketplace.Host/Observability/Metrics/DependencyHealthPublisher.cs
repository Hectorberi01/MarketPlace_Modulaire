using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Marketplace.Host.Observability.Metrics;

/// <summary>
/// Service d'arrière-plan qui exécute périodiquement les health checks enregistrés
/// et publie leur résultat dans la jauge <c>dependency_health_status</c> (via
/// <see cref="AvailabilityMetrics.SetDependencyStatus"/>).
///
/// Chaque health check devient une série <c>dependency_health_status{dependency="&lt;nom&gt;"}</c> :
/// il suffit d'enregistrer un health check (postgres, redis, kafka, payment-provider…)
/// pour qu'il apparaisse automatiquement — aucune modification ici.
///
/// Correspondance des statuts :
/// <list type="bullet">
///   <item><see cref="HealthStatus.Healthy"/> → 1</item>
///   <item><see cref="HealthStatus.Degraded"/> → 0.5</item>
///   <item><see cref="HealthStatus.Unhealthy"/> → 0</item>
/// </list>
/// </summary>
public sealed class DependencyHealthPublisher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly HealthCheckService _healthCheckService;
    private readonly AvailabilityMetrics _metrics;

    public DependencyHealthPublisher(HealthCheckService healthCheckService, AvailabilityMetrics metrics)
    {
        _healthCheckService = healthCheckService;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var report = await _healthCheckService.CheckHealthAsync(stoppingToken);
                foreach (var (name, entry) in report.Entries)
                {
                    _metrics.SetDependencyStatus(name, ToGaugeValue(entry.Status));
                }
            }
            catch (OperationCanceledException)
            {
                break; // arrêt normal de l'application.
            }
            catch
            {
                // Ne jamais faire tomber le service pour une erreur de sonde ;
                // le prochain cycle réessaiera.
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static double ToGaugeValue(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => AvailabilityMetrics.Healthy,
        HealthStatus.Degraded => AvailabilityMetrics.Degraded,
        _ => AvailabilityMetrics.Unhealthy,
    };

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
