using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Inventory.Application.Abstractions;
using Marketplace.Modules.Inventory.Domain.Locations;
using Marketplace.Modules.Inventory.Domain.Stock;

namespace Marketplace.Modules.Inventory.Infrastructure.Persistence;

/// <summary>DbContext du module Inventory (schéma « inventory »).</summary>
public sealed class InventoryDbContext : ModuleDbContext, IInventoryUnitOfWork
{
    public const string SchemaName = "inventory";

    public InventoryDbContext(
        DbContextOptions<InventoryDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<FulfillmentLocation> FulfillmentLocations => Set<FulfillmentLocation>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
