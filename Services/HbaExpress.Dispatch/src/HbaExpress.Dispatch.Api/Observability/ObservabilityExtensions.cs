using System.Reflection;
using HbaExpress.Dispatch.Api.Extensions;
using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace HbaExpress.Dispatch.Api.Observability;

/// <summary>
/// L'observabilité du service de dispatch.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE SERVICE ÉTAIT AVEUGLE, ET C'EST CELUI QU'IL FAUT LE PLUS REGARDER.
///
/// Il tient deux choses qu'aucun humain ne voit passer : des courses qu'il faut trouver
/// preneur en quelques minutes, et des rémunérations à verser. Ses deux pannes coûteuses
/// sont silencieuses par nature — une course que personne n'accepte, et une course livrée
/// dont l'annonce de paiement s'est perdue. Ni l'une ni l'autre ne produit d'erreur HTTP :
/// il n'y a pas de client au bout du fil pour s'en plaindre.
///
/// Le monolithe a Serilog, OpenTelemetry et une alerte qui réveille quelqu'un dès la
/// première lettre morte. Ce service n'avait rien — pas même un journal structuré. On lui
/// donne la même chose, à sa taille.
///
/// MÊME FORME QUE CÔTÉ MONOLITHE, DÉLIBÉRÉMENT : mêmes logs JSON compacts, même export
/// OTLP, même découpage liveness / readiness. Deux services qui parlent deux langues
/// d'observabilité obligent à tenir deux tableaux de bord, et l'un des deux finit
/// toujours par ne plus être regardé.
///
/// SANS COLLECTEUR OTLP CONFIGURÉ, l'instrumentation tourne à vide, sans réseau. Rien à
/// désactiver en développement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddDispatchObservability(
        this WebApplicationBuilder builder, string serviceName)
    {
        // 1. Journaux structurés JSON sur stdout — prêts pour Loki.
        builder.Host.UseSerilog((context, _, config) => config
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", serviceName)
            .WriteTo.Console(new RenderedCompactJsonFormatter()));

        // 2. Sondes. La base est la SEULE dépendance dure : sans elle, ce service ne peut
        //    ni créer, ni offrir, ni accepter une course.
        builder.Services.AddHealthChecks()
            .AddNpgSql(
                DispatchInfrastructureServices.ResolveConnectionString(builder.Configuration),
                name: "dispatch-db",
                tags: new[] { "ready" })
            // Les abandons de la file sortante : des courses livrées que personne n'a
            // payées. Dégradé, pas en panne — redémarrer n'y changerait rien, il faut
            // qu'un humain regarde.
            .AddCheck<OutboundHealthCheck>("dispatch-outbound", tags: new[] { "ready" });

        // 3. Compteurs métier.
        builder.Services.AddMetrics();
        builder.Services.AddSingleton<DispatchMetrics>();
        builder.Services.AddSingleton<IDispatchMetrics>(sp => sp.GetRequiredService<DispatchMetrics>());

        // 4. Traces et métriques OpenTelemetry.
        var otlpEndpoint = builder.Configuration["Otel:Endpoint"]
            ?? builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: version))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    // L'instrumentation du client HTTP compte : c'est par elle qu'on voit
                    // les appels sortants vers la marketplace, et leur latence.
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
                    .AddMeter(DispatchMeters.All);

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return builder;
    }

    /// <summary>
    /// Journalisation des requêtes et sondes de santé, à brancher dans le pipeline.
    /// </summary>
    public static WebApplication MapDispatchObservability(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                // Le COMPTE du coursier, pas son identifiant de coursier : c'est ce que
                // porte le jeton, et c'est donc ce qu'on peut journaliser sans lecture
                // supplémentaire. En champ JSON, pas en étiquette — la cardinalité d'un
                // identifiant utilisateur ferait exploser Prometheus.
                var userId = httpContext.User?.FindFirst("sub")?.Value
                    ?? httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                if (!string.IsNullOrEmpty(userId))
                {
                    diagnosticContext.Set("user_id", userId);
                }
            };
        });

        // Instancie le meter au démarrage : sans cela, ses instruments n'existent qu'au
        // premier événement, et un service parfaitement sain n'apparaîtrait nulle part.
        app.Services.GetRequiredService<DispatchMetrics>();

        // ─────────────────────────────────────────────────────────────────────────
        // AllowAnonymous EST INDISPENSABLE : la politique d'autorisation par repli
        // exige un utilisateur authentifié sur toute route qui ne dit rien. Sans cette
        // exemption, les sondes recevraient un 401 — Docker déclarerait le conteneur
        // malsain et le redémarrerait en boucle. Sécuriser le service le mettrait hors
        // ligne.
        // ─────────────────────────────────────────────────────────────────────────

        // Liveness : ne lance AUCUN contrôle. Le processus est vivant s'il répond.
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
            .WithTags("System")
            .AllowAnonymous();

        // Readiness : la base, et l'état de la file sortante.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })
            .WithTags("System")
            .AllowAnonymous();

        return app;
    }
}
