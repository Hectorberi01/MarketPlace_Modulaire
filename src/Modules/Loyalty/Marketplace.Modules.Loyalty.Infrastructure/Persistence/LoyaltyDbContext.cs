using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Loyalty.Application.Abstractions;
using Marketplace.Modules.Loyalty.Domain.Accounts;

namespace Marketplace.Modules.Loyalty.Infrastructure.Persistence;

/// <summary>DbContext du module Loyalty (schéma « loyalty »).</summary>
public sealed class LoyaltyDbContext : ModuleDbContext, ILoyaltyUnitOfWork
{
    public const string SchemaName = "loyalty";

    public LoyaltyDbContext(
        DbContextOptions<LoyaltyDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<LoyaltyAccount> Accounts => Set<LoyaltyAccount>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LoyaltyDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
