using System.Reflection;
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
/// </list>
/// Aucun endpoint requis : sans collector OTLP configuré, l'instrumentation tourne
/// sans exporter (surcoût négligeable). Activez l'export via <c>Otel:Endpoint</c>
/// ou la variable d'environnement <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>.
/// </summary>
public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder, string serviceName)
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

        // 3. OpenTelemetry — traces + métriques. Export OTLP uniquement si un
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
                    .AddRuntimeInstrumentation();
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return builder;
    }

    /// <summary>
    /// Journalise chaque requête HTTP (une ligne structurée) et expose les probes :
    /// <c>/health</c> (liveness : le process répond) et <c>/health/ready</c>
    /// (readiness : dépendances OK). À appeler dans le pipeline des hôtes.
    /// </summary>
    public static WebApplication MapObservability(this WebApplication app)
    {
        app.UseSerilogRequestLogging();

        // Liveness : ne lance aucun check (le process est vivant s'il répond).
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
            .WithTags("System");

        // Readiness : dépendances taguées « ready » (Postgres / Redis).
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
            .WithTags("System");

        return app;
    }
}
