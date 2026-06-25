using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Wishlist.Application.Abstractions;
using WishlistAggregate = Marketplace.Modules.Wishlist.Domain.Wishlists.Wishlist;

namespace Marketplace.Modules.Wishlist.Infrastructure.Persistence;

/// <summary>DbContext du module Wishlist (schéma « wishlist »).</summary>
public sealed class WishlistDbContext : ModuleDbContext, IWishlistUnitOfWork
{
    public const string SchemaName = "wishlist";

    public WishlistDbContext(
        DbContextOptions<WishlistDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<WishlistAggregate> Wishlists => Set<WishlistAggregate>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WishlistDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
