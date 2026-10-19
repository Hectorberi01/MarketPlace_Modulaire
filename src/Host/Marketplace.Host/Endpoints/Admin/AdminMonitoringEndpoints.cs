using System.Diagnostics;
using System.Text.Json;

namespace Marketplace.Host.Admin.Endpoints;

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
        var monitoring = app.MapAdminGroup("/api/admin/monitoring", "Admin · Supervision");
        monitoring.MapGet("/health", GetSurfaceHealthAsync);
        monitoring.MapGet("/metrics", GetMetricsAsync);
        monitoring.MapGet("/containers", GetContainersAsync);
        monitoring.MapGet("/config", GetConfigAsync);
        monitoring.MapGet("/env", GetConfigViewAsync);
        monitoring.MapGet("/logs/sources", GetLogSourcesAsync);
        monitoring.MapGet("/logs", GetLogsAsync);
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
            portainerUrl = config["Monitoring:PortainerUrl"],
            logsAvailable = !string.IsNullOrWhiteSpace(config["Monitoring:LokiUrl"])
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
    /// Aucun accès au socket Docker : on se contente de LIRE des métriques déjà
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

    // ---- Logs (Loki) ----

    /// <summary>
    /// Liste des sources de logs (valeurs du label « service » de Loki ; repli sur
    /// « container »). Alimente le menu déroulant du panneau Logs de la console.
    /// </summary>
    private static async Task<IResult> GetLogSourcesAsync(
        IHttpClientFactory httpFactory, IConfiguration config, CancellationToken ct)
    {
        var lokiUrl = config["Monitoring:LokiUrl"];
        if (string.IsNullOrWhiteSpace(lokiUrl))
        {
            return Results.Ok(new { configured = false, label = "service", sources = Array.Empty<string>() });
        }

        var client = httpFactory.CreateClient(HttpClientName);
        var baseUrl = lokiUrl.TrimEnd('/');
        var now = DateTimeOffset.UtcNow;
        var start = now.AddHours(-6);

        var label = "service";
        var sources = await LokiLabelValuesAsync(client, baseUrl, label, start, now, ct);
        if (sources.Count == 0)
        {
            label = "container";
            sources = await LokiLabelValuesAsync(client, baseUrl, label, start, now, ct);
        }

        return Results.Ok(new { configured = true, label, sources });
    }

    /// <summary>
    /// Lignes de logs d'un conteneur/service via l'API Loki (query_range). Lecture
    /// seule. Filtres : niveau (label « level ») et recherche plein-texte (|=).
    /// </summary>
    private static async Task<IResult> GetLogsAsync(
        IHttpClientFactory httpFactory, IConfiguration config,
        string? target, string? label, string? level, string? q, int? minutes, int? limit,
        CancellationToken ct)
    {
        var lokiUrl = config["Monitoring:LokiUrl"];
        if (string.IsNullOrWhiteSpace(lokiUrl))
        {
            return Results.Ok(new { configured = false, lines = Array.Empty<object>() });
        }
        if (string.IsNullOrWhiteSpace(target))
        {
            return Results.Ok(new { configured = true, lines = Array.Empty<object>() });
        }

        var client = httpFactory.CreateClient(HttpClientName);
        var baseUrl = lokiUrl.TrimEnd('/');
        var now = DateTimeOffset.UtcNow;
        var start = now.AddMinutes(-Math.Clamp(minutes ?? 60, 1, 1440));
        var take = Math.Clamp(limit ?? 300, 1, 1000);

        // Sélecteur de flux : label imposé (service|container), valeur échappée.
        var labelKey = label == "container" ? "container" : "service";
        var selector = $"{labelKey}=\"{EscapeLabelValue(target)}\"";
        if (!string.IsNullOrWhiteSpace(level) && !level.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            selector += $", level=\"{EscapeLabelValue(level)}\"";
        }
        var query = "{" + selector + "}";
        if (!string.IsNullOrWhiteSpace(q))
        {
            query += $" |= \"{EscapeLabelValue(q)}\"";
        }

        var lines = await LokiQueryRangeAsync(client, baseUrl, query, start, now, take, ct);
        return Results.Ok(new { configured = true, query, count = lines.Count, lines });
    }

    /// <summary>Échappe une valeur pour l'insérer dans une chaîne LogQL entre guillemets.</summary>
    private static string EscapeLabelValue(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");

    /// <summary>Loki : valeurs distinctes d'un label sur la fenêtre donnée (triées).</summary>
    private static async Task<List<string>> LokiLabelValuesAsync(
        HttpClient client, string baseUrl, string label, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var result = new List<string>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var url = $"{baseUrl}/loki/api/v1/label/{label}/values" +
                      $"?start={start.ToUnixTimeMilliseconds()}000000&end={end.ToUnixTimeMilliseconds()}000000";
            using var resp = await client.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                return result;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in data.EnumerateArray())
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        result.Add(s);
                    }
                }
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Best-effort : Loki indisponible → liste vide (le panneau affiche « — »).
        }
        return result;
    }

    /// <summary>
    /// Loki : exécute une requête LogQL (query_range, sens antichronologique) et
    /// projette chaque ligne. Pour les logs .NET (JSON Serilog compact), on extrait
    /// le message « @m » ; sinon on renvoie la ligne brute.
    /// </summary>
    private static async Task<List<object>> LokiQueryRangeAsync(
        HttpClient client, string baseUrl, string query, DateTimeOffset start, DateTimeOffset end, int limit, CancellationToken ct)
    {
        var buffer = new List<(long ts, object item)>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var url = $"{baseUrl}/loki/api/v1/query_range?query={Uri.EscapeDataString(query)}" +
                      $"&start={start.ToUnixTimeMilliseconds()}000000&end={end.ToUnixTimeMilliseconds()}000000" +
                      $"&limit={limit}&direction=backward";
            using var resp = await client.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                return new List<object>();
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            // { data: { result: [ { stream: {labels}, values: [ [ "<ns>", "<line>" ], … ] } ] } }
            foreach (var series in doc.RootElement.GetProperty("data").GetProperty("result").EnumerateArray())
            {
                var labels = series.GetProperty("stream");
                var container = labels.TryGetProperty("container", out var c) ? c.GetString() : null;
                var labelLevel = labels.TryGetProperty("level", out var l) ? l.GetString() : null;
                var std = labels.TryGetProperty("stream", out var st) ? st.GetString() : null;

                foreach (var val in series.GetProperty("values").EnumerateArray())
                {
                    if (!long.TryParse(val[0].GetString(), out var tsNs))
                    {
                        continue;
                    }
                    var raw = val[1].GetString() ?? string.Empty;
                    var (message, parsedLevel) = ExtractMessage(raw);
                    buffer.Add((tsNs, new
                    {
                        ts = DateTimeOffset.FromUnixTimeMilliseconds(tsNs / 1_000_000).ToString("o"),
                        level = labelLevel ?? parsedLevel,
                        container,
                        stream = std,
                        message,
                        raw
                    }));
                }
            }
        }
        catch
        {
            // Best-effort : ne jamais faire échouer la page pour un souci de logs.
        }

        return buffer.OrderByDescending(x => x.ts).Take(limit).Select(x => x.item).ToList();
    }

    /// <summary>
    /// Extrait un message lisible d'une ligne. Logs .NET = JSON compact Serilog
    /// (CLEF) : clé « @m » (message rendu) ou « @mt » (template), « @l » (niveau ;
    /// absent pour Information). Sinon, on renvoie la ligne telle quelle.
    /// </summary>
    private static (string message, string? level) ExtractMessage(string raw)
    {
        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var m = root.TryGetProperty("@m", out var mp) ? mp.GetString()
                      : root.TryGetProperty("@mt", out var mt) ? mt.GetString()
                      : null;
                var lvl = root.TryGetProperty("@l", out var lp) ? lp.GetString() : null;
                if (!string.IsNullOrWhiteSpace(m))
                {
                    return (m!, lvl);
                }
            }
            catch
            {
                // Pas du JSON valide → ligne brute.
            }
        }
        return (raw, null);
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
