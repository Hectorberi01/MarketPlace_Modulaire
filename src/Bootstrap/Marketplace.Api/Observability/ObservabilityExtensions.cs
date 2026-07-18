using System.Reflection;
using Marketplace.Api.Observability.Metrics;
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

namespace Marketplace.Api.Observability;

/// <summary>
/// Base d'observabilité mutualisée pour tous les hôtes (API + BFF) :
/// <list type="bullet">
///   <item>Logs structurés JSON (Serilog) sur stdout — prêts pour Loki / Seq.</item>
///   <item>Traces + métriques (OpenTelemetry), exportées en OTLP si un collector
///         est configuré (Prometheus / Tempo / Grafana Alloy).</item>
///   <item>Health checks de dépendances (Postgres + Redis) : /health (liveness)
///         et /health/ready (readiness).</item>
///   <item>Métriques custom : disponibilité (api_up, uptime, redémarrages, santé des
///         dépendances), sécurité (401/403/429), paiements et métier. Les QUATRE sont
///         enregistrées sur TOUS les hôtes : chaque BFF compose les mêmes modules en
///         processus et exécute les mêmes handlers, qui les réclament par injection.</item>
/// </list>
/// Aucun endpoint requis : sans collector OTLP configuré, l'instrumentation tourne
/// sans exporter (surcoût négligeable). Activez l'export via <c>Otel:Endpoint</c>
/// ou la variable d'environnement <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>.
/// </summary>
public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddObservability(
        this WebApplicationBuilder builder,
        string serviceName)
    {
        // 1. Logs structurés (Serilog) — JSON compact sur la console (stdout).
        builder.Host.UseSerilog((context, _, config) => config
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", serviceName)
            .WriteTo.Console(new RenderedCompactJsonFormatter()));

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
        // ⚠️ LES QUATRE JEUX DE MÉTRIQUES SONT ENREGISTRÉS SUR TOUS LES HÔTES.
        //
        // Il y avait ici un drapeau `businessMetrics`, faux par défaut, qui n'activait
        // Payments et Business que sur l'API. Son commentaire disait :
        //
        //     « Payments + Business seulement là où la logique métier tourne (API). »
        //
        // C'est une prémisse FAUSSE, et elle a coûté cher. Dans ce monolithe modulaire,
        // chaque BFF compose les 25 modules EN PROCESSUS (ModuleRegistry.Installers) et
        // exécute exactement les mêmes handlers MediatR. La logique métier tourne donc
        // dans les BFF autant que dans l'API — c'est même tout l'intérêt du montage.
        //
        // Conséquence concrète : `RegisterUserCommandHandler` réclame
        // `IMarketplaceBusinessMetrics` dans son constructeur. Sur le BFF Admin, le
        // service n'était pas enregistré. MediatR ne résolvait le handler qu'au moment
        // d'exécuter la commande — donc AUCUNE erreur au démarrage, aucun avertissement,
        // rien. Le trou ne s'ouvrait qu'à la première création de compte, en produisant
        // un 500 dont le navigateur ne rapportait qu'une erreur CORS. Trois heures de
        // chasse pour un `if`.
        //
        // Enregistrer quatre singletons inutilisés ne coûte rien. Un handler qui explose
        // en production, si.
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
    /// Journalise chaque requête HTTP (une ligne structurée), branche le middleware
    /// de métriques de sécurité (401/403/429) et expose les probes :
    /// <c>/health</c> (liveness) et <c>/health/ready</c> (readiness).
    /// À appeler dans le pipeline des hôtes.
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

        // ⚠️ `AllowAnonymous()` EST INDISPENSABLE ICI.
        //
        // Les hôtes appliquent désormais une FallbackPolicy qui exige un utilisateur
        // authentifié sur toute route ne déclarant pas d'autorisation. Sans cette
        // exemption explicite, les sondes de santé recevraient un 401 :
        //   • Docker déclarerait le conteneur MALSAIN et le redémarrerait en boucle ;
        //   • Traefik le retirerait du pool ;
        //   • Prometheus le croirait mort.
        // Autrement dit : sécuriser l'API aurait mis la plateforme hors ligne.
        //
        // Ces endpoints ne divulguent rien (liveness ne lance aucun check ; readiness
        // ne renvoie qu'un état agrégé) et ne sont, de toute façon, joignables que
        // depuis le réseau interne — Traefik ne route plus que les webhooks.

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
