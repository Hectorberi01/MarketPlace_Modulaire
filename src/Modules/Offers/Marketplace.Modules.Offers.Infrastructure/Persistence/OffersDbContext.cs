using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Offers.Application.Abstractions;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Infrastructure.Persistence;

/// <summary>DbContext du module Offers (schéma « offers »).</summary>
public sealed class OffersDbContext : ModuleDbContext, IOffersUnitOfWork
{
    public const string SchemaName = "offers";

    public OffersDbContext(
        DbContextOptions<OffersDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Offer> Offers => Set<Offer>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OffersDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
