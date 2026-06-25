using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Billing.Application.Abstractions;
using Marketplace.Modules.Billing.Domain.Commissions;
using Marketplace.Modules.Billing.Domain.Invoices;

namespace Marketplace.Modules.Billing.Infrastructure.Persistence;

/// <summary>DbContext du module Billing (schéma « billing »).</summary>
public sealed class BillingDbContext : ModuleDbContext, IBillingUnitOfWork
{
    public const string SchemaName = "billing";

    public BillingDbContext(
        DbContextOptions<BillingDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<CommissionRule> CommissionRules => Set<CommissionRule>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
