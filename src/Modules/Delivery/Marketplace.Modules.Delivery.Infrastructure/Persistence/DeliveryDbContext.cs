using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Delivery.Application.Abstractions;
using Marketplace.Modules.Delivery.Domain.Couriers;

namespace Marketplace.Modules.Delivery.Infrastructure.Persistence;

/// <summary>DbContext du module Delivery (schéma « delivery »).</summary>
public sealed class DeliveryDbContext : ModuleDbContext, IDeliveryUnitOfWork
{
    public const string SchemaName = "delivery";

    public DeliveryDbContext(
        DbContextOptions<DeliveryDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Courier> Couriers => Set<Courier>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeliveryDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
