using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Analytics.Application.Metrics;
using Marketplace.Modules.Analytics.Domain.Metrics;

namespace Marketplace.Modules.Analytics.Infrastructure.Persistence;

/// <summary>DbContext du module Analytics (schéma « analytics »).</summary>
public sealed class AnalyticsDbContext : ModuleDbContext, IAnalyticsUnitOfWork
{
    public const string SchemaName = "analytics";

    public AnalyticsDbContext(
        DbContextOptions<AnalyticsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<MetricSnapshot> Snapshots => Set<MetricSnapshot>();
    public DbSet<ProcessedOrder> ProcessedOrders => Set<ProcessedOrder>();
    public DbSet<ProcessedRefund> ProcessedRefunds => Set<ProcessedRefund>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnalyticsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
