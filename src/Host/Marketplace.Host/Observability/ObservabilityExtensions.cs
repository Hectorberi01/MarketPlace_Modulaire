using System.Reflection;
using Marketplace.Host.Observability.Metrics;
using Marketplace.BuildingBlocks.Application.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace Marketplace.Host.Observability;

/// <summary>
/// Base d'observabilité mutualisée pour tous les hôtes (API + BFF) : logs JSON Serilog
/// sur stdout, traces et métriques OpenTelemetry exportées en OTLP, health checks
/// Postgres + Redis (<c>/health</c> liveness, <c>/health/ready</c> readiness), et quatre
/// jeux de métriques custom — disponibilité, sécurité, paiements, métier.
///
/// Sans collector OTLP configuré, l'instrumentation tourne sans exporter. Export activé
/// par <c>Otel:Endpoint</c> ou <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>.
/// </summary>
public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddObservability(
        this WebApplicationBuilder builder,
        string serviceName)
    {
        // 1. Logs structurés (Serilog).
        //
        // DEUX FORMATS, PARCE QU'IL Y A DEUX LECTEURS. En production, c'est Loki qui
        // lit : du JSON compact, une ligne par événement, propriétés indexables. En
        // développement, c'est un humain devant `docker compose up` : une ligne de
        // JSON de 2 000 caractères par requête SQL y est illisible, et illisible
        // signifie non lu.
        //
        // LES NIVEAUX SE RÈGLENT DANS LA SECTION « Serilog » des appsettings, pas
        // dans « Logging:LogLevel ». UseSerilog remplace tout le pipeline de
        // journalisation, et ReadFrom.Configuration ne lit que « Serilog ».
        builder.Host.UseSerilog((context, _, config) =>
        {
            config
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service", serviceName);

            if (context.HostingEnvironment.IsDevelopment())
            {
                config.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
            }
            else
            {
                config.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
        });

        // 2. Health checks de dépendances (tag « ready » = readiness).
        var health = builder.Services.AddHealthChecks();

        var postgres = builder.Configuration.GetConnectionString("Marketplace");
        if (!string.IsNullOrWhiteSpace(postgres))
        {
            health.AddNpgSql(postgres, name: "postgres", tags: new[] { "ready" });
        }

        var redis = builder.Configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redis))
        {
            health.AddRedis(redis, name: "redis", tags: new[] { "ready" });
        }

        // 3. Métriques custom (System.Diagnostics.Metrics via IMeterFactory).
        //
        // LES QUATRE JEUX SONT ENREGISTRÉS SUR TOUS LES HÔTES. Chaque BFF compose tous
        // les modules EN PROCESSUS et exécute les mêmes handlers : la logique métier
        // tourne dans les BFF autant que dans l'API. Un service manquant ne se voit pas
        // au démarrage — MediatR ne résout le handler qu'à l'exécution de la commande, et
        // le 500 n'arrive qu'au premier appel. Quatre singletons inutilisés ne coûtent rien.
        builder.Services.AddMetrics();
        builder.Services.AddSingleton<AvailabilityMetrics>();
        builder.Services.AddSingleton<SecurityMetrics>();
        builder.Services.AddSingleton<PaymentMetrics>();
        builder.Services.AddSingleton<MarketplaceBusinessMetrics>();
        builder.Services.AddSingleton<OutboxMetrics>();

        // Interfaces (consommées par les modules via BuildingBlocks) → mêmes instances.
        builder.Services.AddSingleton<ISecurityMetrics>(sp => sp.GetRequiredService<SecurityMetrics>());
        builder.Services.AddSingleton<IPaymentMetrics>(sp => sp.GetRequiredService<PaymentMetrics>());
        builder.Services.AddSingleton<IMarketplaceBusinessMetrics>(sp => sp.GetRequiredService<MarketplaceBusinessMetrics>());
        builder.Services.AddSingleton<IOutboxMetrics>(sp => sp.GetRequiredService<OutboxMetrics>());

        builder.Services.AddHostedService<DependencyHealthPublisher>();

        // 4. OpenTelemetry — traces + métriques. Export OTLP uniquement si un
        //    collector est configuré (sinon instrumentation « à vide », sans réseau).
        var otlpEndpoint = builder.Configuration["Otel:Endpoint"]
            ?? builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: version))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    // Meters custom de la plateforme (dispo, paiements, métier, sécurité).
                    .AddMeter(MarketplaceMeters.All);
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return builder;
    }

    /// <summary>
    /// Journalise chaque requête HTTP, branche le middleware de métriques de sécurité
    /// (401/403/429) et expose <c>/health</c> (liveness) et <c>/health/ready</c>
    /// (readiness). À appeler dans le pipeline des hôtes.
    /// </summary>
    public static WebApplication MapObservability(this WebApplication app)
    {
        // Le log de fin de requête HTTP porte l'utilisateur authentifié (user_id),
        // pour corréler dans Loki les appels d'un même compte — y compris ceux qui
        // ne passent pas par MediatR. Champ JSON, pas un label (cardinalité).
        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                var userId = httpContext.User?.FindFirst("sub")?.Value
                    ?? httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    diagnosticContext.Set("user_id", userId);
                }
            };
        });

        // Compte 401/403/429 (par motif de route, pas d'URL brute). Placé tôt : il
        // inspecte le code de statut au retour, après tout le pipeline en aval.
        app.UseMiddleware<SecurityMetricsMiddleware>();

        // Instancie les meters custom au démarrage pour que leurs instruments
        // (jauges observables notamment) soient enregistrés dès le premier scrape.
        app.Services.GetRequiredService<AvailabilityMetrics>();
        app.Services.GetRequiredService<SecurityMetrics>();
        app.Services.GetService<PaymentMetrics>();
        app.Services.GetService<MarketplaceBusinessMetrics>();

        // `AllowAnonymous()` EST INDISPENSABLE : sans cette exemption, la FallbackPolicy
        // exige un utilisateur authentifié, les sondes reçoivent un 401, Docker déclare le
        // conteneur malsain et le redémarre en boucle pendant que Traefik le retire du
        // pool. Ces endpoints ne divulguent rien et ne sont joignables que du réseau
        // interne.

        // Liveness : ne lance aucun check (le process est vivant s'il répond).
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
            .WithTags("System")
            .AllowAnonymous();

        // Readiness : dépendances taguées « ready » (Postgres / Redis).
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
            .WithTags("System")
            .AllowAnonymous();

        return app;
    }
}
