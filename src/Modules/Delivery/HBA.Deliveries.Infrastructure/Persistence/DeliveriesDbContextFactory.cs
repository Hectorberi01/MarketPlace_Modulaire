using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HBA.Deliveries.Infrastructure.Persistence;

/// <summary>
/// Factory design-time pour les outils EF (`dotnet ef migrations add`).
///
/// Elle n'est utilisée QUE par la ligne de commande : au démarrage réel, c'est
/// l'installer du module qui construit le DbContext. D'où le répartiteur d'events
/// inerte — générer une migration ne doit déclencher aucun effet de bord.
/// </summary>
public sealed class DeliveriesDbContextFactory : IDesignTimeDbContextFactory<DeliveriesDbContext>
{
    public DeliveriesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DELIVERIES_DB")
            ?? "Host=localhost;Port=5432;Database=marketplace;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<DeliveriesDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DeliveriesDbContext.SchemaName))
            .Options;

        return new DeliveriesDbContext(options, NoOpDomainEventDispatcher.Instance, new IntegrationEventQueue());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public static readonly NoOpDomainEventDispatcher Instance = new();

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
