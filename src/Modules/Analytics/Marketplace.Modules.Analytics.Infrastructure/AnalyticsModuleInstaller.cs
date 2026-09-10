using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Analytics.Application.Metrics;
using Marketplace.Modules.Analytics.Domain.Metrics;
using Marketplace.Modules.Analytics.Infrastructure.Persistence;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Contracts.IntegrationEvents;
using Marketplace.Modules.Returns.Contracts.IntegrationEvents;
using Marketplace.Modules.Payments.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Analytics.Infrastructure;

/// <summary>Enregistre le module Analytics : DbContext read model, repository, accrual consumer, outbox.</summary>
public sealed class AnalyticsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Analytics";

    public Assembly ApplicationAssembly => typeof(GetReportQuery).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<AnalyticsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", AnalyticsDbContext.SchemaName)));

        services.AddScoped<IAnalyticsUnitOfWork>(sp => sp.GetRequiredService<AnalyticsDbContext>());

        services.AddScoped<IMetricSnapshotRepository, MetricSnapshotRepository>();

        // Chorégraphie : accumulation des métriques à la confirmation de commande.
        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>, AccrueMetricsOnOrderConfirmedHandler>();

        // ═══════════════════════════════════════════════════════════════════════════
        // ET LA CORRECTION, QUI N'EXISTAIT PAS.
        //
        // Ce module n'avait qu'un écrivain, sur un seul événement : le chiffre
        // d'affaires ne pouvait que monter. Un article retourné et remboursé y restait
        // pour toujours. Les trois voies par lesquelles de l'argent revient à un client
        // sont désormais écoutées ; la troisième a dû être créée côté Settlement, qui
        // n'annonçait rien.
        //
        // L'ANNULATION N'EST PAS DE LA PARTIE, ET C'EST NORMAL : `Order.Cancel` est
        // refusée dès `Confirmed`, or l'accumulation n'a lieu qu'à `Confirm`. Une
        // commande annulée n'a donc jamais été comptée — il n'y a rien à déduire.
        // ═══════════════════════════════════════════════════════════════════════════
        services.AddScoped<ImputationRemboursement>();
        services.AddScoped<IIntegrationEventHandler<ReturnRefundedIntegrationEvent>, DeduireMetriquesSurRetourRembourseHandler>();
        services.AddScoped<IIntegrationEventHandler<PaymentRefundedIntegrationEvent>, DeduireMetriquesSurPaiementRembourseHandler>();
        services.AddScoped<IIntegrationEventHandler<CustomerRefundSettledIntegrationEvent>, DeduireMetriquesSurGesteCommercialHandler>();

        services.AddOutboxProcessor<AnalyticsDbContext>();
    }
}
