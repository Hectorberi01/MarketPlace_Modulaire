using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Shipping.Application.Abstractions;
using Marketplace.Modules.Shipping.Domain.Carriers;
using Marketplace.Modules.Shipping.Domain.Shipments;

namespace Marketplace.Modules.Shipping.Infrastructure.Persistence;

/// <summary>DbContext du module Shipping (schéma « shipping »).</summary>
public sealed class ShippingDbContext : ModuleDbContext, IShippingUnitOfWork
{
    public const string SchemaName = "shipping";

    public ShippingDbContext(
        DbContextOptions<ShippingDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Carrier> Carriers => Set<Carrier>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShippingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
