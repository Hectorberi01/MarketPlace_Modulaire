using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Disputes.Application.Abstractions;
using Marketplace.Modules.Disputes.Domain.Disputes;

namespace Marketplace.Modules.Disputes.Infrastructure.Persistence;

/// <summary>DbContext du module Disputes (schéma « disputes »).</summary>
public sealed class DisputesDbContext : ModuleDbContext, IDisputesUnitOfWork
{
    public const string SchemaName = "disputes";

    public DisputesDbContext(
        DbContextOptions<DisputesDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Dispute> Disputes => Set<Dispute>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DisputesDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
