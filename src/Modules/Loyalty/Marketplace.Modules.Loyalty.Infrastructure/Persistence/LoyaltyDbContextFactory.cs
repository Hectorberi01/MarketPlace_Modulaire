using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;

namespace Marketplace.Modules.Loyalty.Infrastructure.Persistence;

/// <summary>Factory design-time pour les outils EF.</summary>
public sealed class LoyaltyDbContextFactory : IDesignTimeDbContextFactory<LoyaltyDbContext>
{
    public LoyaltyDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LOYALTY_DB")
            ?? "Host=localhost;Port=5432;Database=marketplace;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<LoyaltyDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", LoyaltyDbContext.SchemaName))
            .Options;

        return new LoyaltyDbContext(options, NoOpDomainEventDispatcher.Instance, new IntegrationEventQueue());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public static readonly NoOpDomainEventDispatcher Instance = new();

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
