using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Catalog.Domain.Brands;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// DbContext du module Catalog. Vit dans le schéma « catalog » : pas de JOIN ni
/// de foreign key vers un autre schéma. Hérite de ModuleDbContext pour l'Unit of
/// Work (dispatch des domain events) et l'outbox.
/// </summary>
public sealed class CatalogDbContext : ModuleDbContext, ICatalogUnitOfWork
{
    public const string SchemaName = "catalog";

    public CatalogDbContext(
        DbContextOptions<CatalogDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Category> Categories => Set<Category>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Applique d'abord les configs du module (Product…), puis la base
        // (schéma par défaut + outbox).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
