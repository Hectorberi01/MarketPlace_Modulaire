using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Recommendations.Application.Recommendations;
using Marketplace.Modules.Recommendations.Domain.Recommendations;

namespace Marketplace.Modules.Recommendations.Infrastructure.Persistence;

/// <summary>DbContext du module Recommendations (schéma « recommendations »).</summary>
public sealed class RecommendationsDbContext : ModuleDbContext, IRecommendationsUnitOfWork
{
    public const string SchemaName = "recommendations";

    public RecommendationsDbContext(
        DbContextOptions<RecommendationsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Recommendation> Recommendations => Set<Recommendation>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RecommendationsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
