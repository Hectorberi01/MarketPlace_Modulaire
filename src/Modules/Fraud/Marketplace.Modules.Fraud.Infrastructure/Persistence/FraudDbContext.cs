using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Fraud.Application.Risk;
using Marketplace.Modules.Fraud.Domain.Risk;

namespace Marketplace.Modules.Fraud.Infrastructure.Persistence;

/// <summary>DbContext du module Fraud (schéma « fraud »).</summary>
public sealed class FraudDbContext : ModuleDbContext, IFraudUnitOfWork
{
    public const string SchemaName = "fraud";

    public FraudDbContext(
        DbContextOptions<FraudDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<RiskAssessment> Assessments => Set<RiskAssessment>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FraudDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
