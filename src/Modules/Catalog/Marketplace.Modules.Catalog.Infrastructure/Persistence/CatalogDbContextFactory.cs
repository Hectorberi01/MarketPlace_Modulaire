using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// Factory design-time pour les outils EF (migrations add / database update).
/// Permet d'opérer sur le module sans démarrer toute l'application. La chaîne de
/// connexion peut être surchargée via la variable d'env CATALOG_DB.
/// </summary>
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CATALOG_DB")
            ?? "Host=localhost;Port=5432;Database=marketplace;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", CatalogDbContext.SchemaName))
            .Options;

        return new CatalogDbContext(options, NoOpDomainEventDispatcher.Instance, new IntegrationEventQueue());
    }

    /// <summary>Dispatcher inerte : aucun event n'est dispatché au design-time.</summary>
    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public static readonly NoOpDomainEventDispatcher Instance = new();

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
