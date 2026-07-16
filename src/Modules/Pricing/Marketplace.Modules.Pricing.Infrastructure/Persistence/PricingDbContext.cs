using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Pricing.Application.Abstractions;
using Marketplace.Modules.Pricing.Domain.Promotions;

namespace Marketplace.Modules.Pricing.Infrastructure.Persistence;

/// <summary>DbContext du module Pricing (schéma « pricing »).</summary>
public sealed class PricingDbContext : ModuleDbContext, IPricingUnitOfWork
{
    public const string SchemaName = "pricing";

    public PricingDbContext(
        DbContextOptions<PricingDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Promotion> Promotions => Set<Promotion>();

    public DbSet<PromotionRedemption> PromotionRedemptions => Set<PromotionRedemption>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PricingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
