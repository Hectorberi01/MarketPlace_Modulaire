using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Search.Application.Abstractions;
using Marketplace.Modules.Search.Domain.Documents;

namespace Marketplace.Modules.Search.Infrastructure.Persistence;

/// <summary>DbContext du module Search (schéma « search ») — read model d'indexation.</summary>
public sealed class SearchDbContext : ModuleDbContext, ISearchUnitOfWork
{
    public const string SchemaName = "search";

    public SearchDbContext(
        DbContextOptions<SearchDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<SearchDocument> Documents => Set<SearchDocument>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SearchDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
