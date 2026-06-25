using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Tax.Application.Abstractions;
using Marketplace.Modules.Tax.Domain.Taxes;

namespace Marketplace.Modules.Tax.Infrastructure.Persistence;

/// <summary>DbContext du module Tax (schéma « tax »).</summary>
public sealed class TaxDbContext : ModuleDbContext, ITaxUnitOfWork
{
    public const string SchemaName = "tax";

    public TaxDbContext(
        DbContextOptions<TaxDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<TaxRule> TaxRules => Set<TaxRule>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaxDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
