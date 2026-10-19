using System.Diagnostics.Metrics;

namespace Marketplace.Host.Observability.Metrics;

/// <summary>
/// Métriques de disponibilité de l'application, exposées via un
/// <see cref="Meter"/> nommé <see cref="MarketplaceMeters.Availability"/>.
///
/// <list type="bullet">
///   <item><c>api_up</c> — jauge (1 = process vivant). Toujours 1 tant que le
///         process répond au scrape ; l'absence de la série signale un arrêt.</item>
///   <item><c>application_uptime_seconds</c> — jauge : secondes depuis le démarrage.</item>
///   <item><c>application_restart_total</c> — compteur incrémenté une fois au
///         démarrage. Un <c>increase(application_restart_total[1h])</c> côté
///         Prometheus donne le nombre de redémarrages (le compteur repart de 0
///         à chaque process, ce que Prometheus interprète comme un « reset »).</item>
///   <item><c>dependency_health_status</c> — jauge par dépendance (1 healthy,
///         0.5 degraded, 0 unhealthy). Alimentée par
///         <see cref="DependencyHealthPublisher"/>.</item>
/// </list>
///
/// Enregistré en singleton. Le <see cref="Meter"/> est créé une fois et doit être
/// déclaré auprès d'OpenTelemetry (<c>AddMeter</c>).
/// </summary>
public sealed class AvailabilityMetrics : IDisposable
{
    /// <summary>Valeurs d'état de santé exposées par <c>dependency_health_status</c>.</summary>
    public const double Healthy = 1.0;
    public const double Degraded = 0.5;
    public const double Unhealthy = 0.0;

    private readonly Meter _meter;
    private readonly Counter<long> _restarts;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    // État courant des dépendances (nom → statut), publié via une jauge observable.
    private readonly Dictionary<string, double> _dependencyStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public AvailabilityMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MarketplaceMeters.Availability);

        // api_up : 1 tant que le process est vivant.
        _meter.CreateObservableGauge(
            "api_up",
            () => 1L,
            unit: "{status}",
            description: "1 lorsque l'API est démarrée et répond.");

        // Uptime en secondes (jauge observable, calculée à chaque scrape).
        _meter.CreateObservableGauge(
            "application_uptime_seconds",
            () => (DateTimeOffset.UtcNow - _startedAt).TotalSeconds,
            unit: "s",
            description: "Durée de fonctionnement depuis le démarrage du process.");

        // Compteur de redémarrage : +1 au démarrage. increase()/resets = nb de démarrages.
        _restarts = _meter.CreateCounter<long>(
            "application_restart",
            unit: "{restart}",
            description: "Incrémenté une fois au démarrage (détection de redémarrage via Prometheus).");
        _restarts.Add(1);

        // dependency_health_status : une série par dépendance (label « dependency »).
        _meter.CreateObservableGauge(
            "dependency_health_status",
            ObserveDependencies,
            unit: "{status}",
            description: "État des dépendances : 1 healthy, 0.5 degraded, 0 unhealthy.");
    }

    /// <summary>
    /// Met à jour l'état d'une dépendance (postgres, redis, kafka, payment-provider…).
    /// Appelé périodiquement par <see cref="DependencyHealthPublisher"/>.
    /// </summary>
    public void SetDependencyStatus(string dependency, double status)
    {
        lock (_sync)
        {
            _dependencyStatus[dependency] = status;
        }
    }

    private IEnumerable<Measurement<double>> ObserveDependencies()
    {
        lock (_sync)
        {
            // Copie défensive : l'énumération est consommée hors du lock par OTel.
            var measurements = new List<Measurement<double>>(_dependencyStatus.Count);
            foreach (var (dependency, status) in _dependencyStatus)
            {
                measurements.Add(new Measurement<double>(status, new KeyValuePair<string, object?>("dependency", dependency)));
            }

            return measurements;
        }
    }

    public void Dispose() => _meter.Dispose();
}
