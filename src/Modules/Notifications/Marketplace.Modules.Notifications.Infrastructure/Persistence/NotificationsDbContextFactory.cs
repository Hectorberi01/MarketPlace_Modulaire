using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;

namespace Marketplace.Modules.Notifications.Infrastructure.Persistence;

/// <summary>Factory design-time pour les outils EF.</summary>
public sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOTIFICATIONS_DB")
            ?? "Host=localhost;Port=5432;Database=marketplace;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", NotificationsDbContext.SchemaName))
            .Options;

        return new NotificationsDbContext(options, NoOpDomainEventDispatcher.Instance, new IntegrationEventQueue());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public static readonly NoOpDomainEventDispatcher Instance = new();

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
