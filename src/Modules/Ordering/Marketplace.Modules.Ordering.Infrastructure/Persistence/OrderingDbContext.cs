using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Ordering.Application.Abstractions;
using Marketplace.Modules.Ordering.Domain.Orders;

namespace Marketplace.Modules.Ordering.Infrastructure.Persistence;

/// <summary>DbContext du module Ordering (schéma « ordering »).</summary>
public sealed class OrderingDbContext : ModuleDbContext, IOrderingUnitOfWork
{
    public const string SchemaName = "ordering";

    public OrderingDbContext(
        DbContextOptions<OrderingDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
