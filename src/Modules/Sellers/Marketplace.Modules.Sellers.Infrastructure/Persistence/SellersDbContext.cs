using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Infrastructure.Persistence;

/// <summary>
/// DbContext du module Sellers (schéma « sellers »). Hérite de ModuleDbContext
/// pour l'Unit of Work (dispatch des domain events) et l'outbox.
/// </summary>
public sealed class SellersDbContext : ModuleDbContext, ISellerUnitOfWork
{
    public const string SchemaName = "sellers";

    public SellersDbContext(
        DbContextOptions<SellersDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Seller> Sellers => Set<Seller>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SellersDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
