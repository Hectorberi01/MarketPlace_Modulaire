using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Cart.Application.Abstractions;
using CartAggregate = Marketplace.Modules.Cart.Domain.Carts.Cart;

namespace Marketplace.Modules.Cart.Infrastructure.Persistence;

/// <summary>DbContext du module Cart (schéma « cart »).</summary>
public sealed class CartDbContext : ModuleDbContext, ICartUnitOfWork
{
    public const string SchemaName = "cart";

    public CartDbContext(
        DbContextOptions<CartDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<CartAggregate> Carts => Set<CartAggregate>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CartDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
