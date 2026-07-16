using System.Diagnostics;
using System.Text.Json;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>
/// BFF Admin — supervision. Expose l'état de santé des surfaces (API + BFF) et,
/// si un Prometheus est configuré, quelques KPIs live. Le BFF Admin, sur le
/// réseau interne, interroge lui-même les endpoints /health des autres hôtes :
/// la console n'a pas besoin d'accéder directement à Grafana/Prometheus.
///
/// Config (section « Monitoring ») :
///   "Monitoring": {
///     "Surfaces": [ { "Name": "API", "Url": "http://api:8080" }, … ],
///     "PrometheusUrl": "http://prometheus:9090",
///     "GrafanaUrl": "https://grafana.interne/…"
///   }
/// </summary>
public static class AdminMonitoringEndpoints
{
    private const string HttpClientName = "monitoring";

    public static IEndpointRouteBuilder MapAdminMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var monitoring = app.MapAdminGroup("/admin/monitoring", "Admin · Supervision");
        monitoring.MapGet("/health", GetSurfaceHealthAsync);
        monitoring.MapGet("/metrics", GetMetricsAsync);
        monitoring.MapGet("/containers", GetContainersAsync);
        monitoring.MapGet("/config", GetConfigAsync);
        monitoring.MapGet("/env", GetConfigViewAsync);
        return app;
    }

    /// <summary>Ping /health (liveness) et /health/ready (readiness) de chaque surface.</summary>
    private static async Task<IResult> GetSurfaceHealthAsync(
        IHttpClientFactory httpFactory, IConfiguration config, CancellationToken ct)
    {
        var surfaces = config.GetSection("Monitoring:Surfaces").Get<SurfaceConfig[]>() ?? Array.Empty<SurfaceConfig>();
        var client = httpFactory.CreateClient(HttpClientName);

        var results = new List<object>(surfaces.Length);
        foreach (var s in surfaces)
        {
            var baseUrl = s.Url.TrimEnd('/');
            var (live, liveMs, liveErr) = await ProbeAsync(client, $"{baseUrl}/health", ct);
            var (ready, _, _) = await ProbeAsync(client, $"{baseUrl}/health/ready", ct);
            results.Add(new
            {
                name = s.Name,
                url = s.Url,
                live,
                ready,
                latencyMs = liveMs,
                error = liveErr
            });
        }

        return Results.Ok(results);
    }

    /// <summary>KPIs live via l'API HTTP de Prometheus (si configuré) : débit, taux d'erreur, p95.</summary>
    private static async Task<IResult> GetMetricsAsync(
        IHttpClientFactory httpFactory, IConfiguration config, CancellationToken ct)
    {
        var promUrl = config["Monitoring:PrometheusUrl"];
        if (string.IsNullOrWhiteSpace(promUrl))
        {
            return Results.Ok(new { configured = false });
        }

        var client = httpFactory.CreateClient(HttpClientName);
        var baseUrl = promUrl.TrimEnd('/');

        var requestsPerMin = await QueryScalarAsync(client, baseUrl,
            "sum(rate(http_server_request_duration_seconds_count[5m])) * 60", ct);
        var errorRatePct = await QueryScalarAsync(client, baseUrl,
            "sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~\"5..\"}[5m])) " +
            "/ clamp_min(sum(rate(http_server_request_duration_seconds_count[5m])), 1) * 100", ct);
        var p95Ms = await QueryScalarAsync(client, baseUrl,
            "histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m]))) * 1000", ct);

        return Results.Ok(new
        {
            configured = true,
            requestsPerMin,
            errorRatePct,
            p95Ms
        });
    }

    /// <summary>
    /// Liens externes pour la console : Grafana (bouton + iframe) et Portainer
    /// (gestion des conteneurs). Vides = masqués côté UI.
    /// </summary>
    private static IResult GetConfigAsync(IConfiguration config)
        => Results.Ok(new
        {
            grafanaUrl = config["Monitoring:GrafanaUrl"],
            grafanaEmbedUrl = config["Monitoring:GrafanaEmbedUrl"],
            portainerUrl = config["Monitoring:PortainerUrl"]
        });

    // Clés de config NON sensibles affichées en clair (vue lecture seule).
    private static readonly string[] SafeKeys =
    {
        "ASPNETCORE_ENVIRONMENT",
        "Jwt:Issuer",
        "Jwt:Audience",
        "Jwt:AccessTokenMinutes",
        "RunMigrationsAtStartup",
        "Otel:Endpoint",
        "Monitoring:PrometheusUrl",
        "Monitoring:GrafanaUrl",
        "Payments:FedaPay:EnablePayouts",
        "Storage:CloudflareR2:BucketName",
        "Storage:CloudflareR2Media:PublicBaseUrl",
        "Media:Cloudinary:CloudName",
        "Redis:InstanceName"
    };

    // Clés SENSIBLES : jamais renvoyées en clair — on n'expose que « défini / non défini ».
    private static readonly string[] SecretKeys =
    {
        "Jwt:SigningKey",
        "ConnectionStrings:Marketplace",
        "Redis:ConnectionString",
        "Payments:FedaPay:ApiKey",
        "Payments:FedaPay:WebhookSecret",
        "Storage:CloudflareR2:AccessKeyId",
        "Storage:CloudflareR2:SecretAccessKey",
        "Media:Cloudinary:ApiKey",
        "Media:Cloudinary:ApiSecret"
    };

    /// <summary>
    /// Vue LECTURE SEULE des conteneurs, alimentée par Prometheus / cAdvisor.
    /// ⚠️ Aucun accès au socket Docker : on se contente de LIRE des métriques déjà
    /// collectées. La console ne peut donc ni démarrer, ni arrêter, ni supprimer un
    /// conteneur — c'est volontaire (le socket Docker = équivalent root sur l'hôte).
    ///
    /// Sur ce serveur, cAdvisor identifie les conteneurs par leur IMAGE (le label
    /// « name » contient l'ID brut) : on dérive donc un nom court depuis l'image.
    /// </summary>
    private static async Task<IResult> GetContainersAsync(
        IHttpClientFactory httpFactory, IConfiguration config, CancellationToken ct)
    {
        var promUrl = config["Monitoring:PrometheusUrl"];
        if (string.IsNullOrWhiteSpace(promUrl))
        {
            return Results.Ok(Array.Empty<object>());
        }

        var client = httpFactory.CreateClient(HttpClientName);
        var baseUrl = promUrl.TrimEnd('/');

        var cpu = await QueryVectorAsync(client, baseUrl,
            "sum by (image) (rate(container_cpu_usage_seconds_total{name!=\"\"}[5m]))", "image", ct);
        var memory = await QueryVectorAsync(client, baseUrl,
            "sum by (image) (container_memory_usage_bytes{name!=\"\"})", "image", ct);
        var uptime = await QueryVectorAsync(client, baseUrl,
            "time() - min by (image) (container_start_time_seconds{name!=\"\"})", "image", ct);

        var containers = cpu.Keys
            .Union(memory.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(image => ShortName(image), StringComparer.OrdinalIgnoreCase)
            .Select(image => new
            {
                image,
                name = ShortName(image),
                cpuCores = cpu.TryGetValue(image, out var c) ? c : (double?)null,
                memoryBytes = memory.TryGetValue(image, out var m) ? m : (double?)null,
                uptimeSeconds = uptime.TryGetValue(image, out var u) ? u : (double?)null,
                running = true // présent dans les métriques récentes = en cours d'exécution
            })
            .ToList();

        return Results.Ok(containers);
    }

    /// <summary>« ghcr.io/hectorberi01/bff-admin:staging » → « bff-admin ».</summary>
    private static string ShortName(string image)
    {
        var lastSegment = image.Split('/').LastOrDefault() ?? image;
        var withoutTag = lastSegment.Split(':').FirstOrDefault();
        return string.IsNullOrWhiteSpace(withoutTag) ? image : withoutTag;
    }

    /// <summary>
    /// Vue LECTURE SEULE de la configuration du BFF Admin. Les valeurs sensibles
    /// (clés, secrets, chaînes de connexion) ne sont JAMAIS renvoyées : seul leur
    /// état (défini / non défini) est exposé. La modification passe par le vault
    /// + le pipeline de déploiement, jamais par cette API.
    /// </summary>
    private static IResult GetConfigViewAsync(IConfiguration config)
    {
        var entries = new List<object>(SafeKeys.Length + SecretKeys.Length);
        foreach (var key in SafeKeys)
        {
            entries.Add(new { key, value = config[key] ?? "—", sensitive = false });
        }
        foreach (var key in SecretKeys)
        {
            entries.Add(new { key, value = string.IsNullOrWhiteSpace(config[key]) ? "non défini" : "défini", sensitive = true });
        }
        return Results.Ok(entries);
    }

    // ---- Helpers ----

    private static async Task<(bool ok, long ms, string? error)> ProbeAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var sw = Stopwatch.StartNew();
        try
        {
            using var resp = await client.GetAsync(url, cts.Token);
            sw.Stop();
            return (resp.IsSuccessStatusCode, sw.ElapsedMilliseconds, resp.IsSuccessStatusCode ? null : $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, sw.ElapsedMilliseconds, ex.GetType().Name);
        }
    }

    private static async Task<double?> QueryScalarAsync(HttpClient client, string promBaseUrl, string query, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var url = $"{promBaseUrl}/api/v1/query?query={Uri.EscapeDataString(query)}";
            using var resp = await client.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            // { data: { result: [ { value: [ <ts>, "<val>" ] } ] } }
            var result = doc.RootElement.GetProperty("data").GetProperty("result");
            if (result.GetArrayLength() == 0)
            {
                return null;
            }
            var value = result[0].GetProperty("value")[1].GetString();
            return double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Exécute une requête PromQL renvoyant un VECTEUR et projette le résultat en
    /// dictionnaire « valeur du label &lt;labelKey&gt; » → valeur numérique.
    /// Best-effort : une erreur Prometheus renvoie un dictionnaire vide (la page
    /// affiche alors « — » plutôt que de planter).
    /// </summary>
    private static async Task<Dictionary<string, double>> QueryVectorAsync(
        HttpClient client, string promBaseUrl, string query, string labelKey, CancellationToken ct)
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var url = $"{promBaseUrl}/api/v1/query?query={Uri.EscapeDataString(query)}";
            using var resp = await client.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                return map;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            // { data: { result: [ { metric: { image: "…" }, value: [ <ts>, "<val>" ] } ] } }
            foreach (var series in doc.RootElement.GetProperty("data").GetProperty("result").EnumerateArray())
            {
                if (!series.TryGetProperty("metric", out var metric) ||
                    !metric.TryGetProperty(labelKey, out var label))
                {
                    continue;
                }

                var key = label.GetString();
                var raw = series.GetProperty("value")[1].GetString();
                if (!string.IsNullOrWhiteSpace(key) &&
                    double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var value))
                {
                    map[key] = value;
                }
            }
        }
        catch
        {
            // Best-effort : ne jamais faire échouer la supervision pour une sonde.
        }

        return map;
    }

    private sealed record SurfaceConfig(string Name, string Url);
}
