using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Serialization;

namespace Marketplace.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Base de tous les DbContext de module. Apporte :
///  - l'Unit of Work (SaveChanges qui dispatche les domain events) ;
///  - le drainage de la file d'events d'intégration vers l'outbox local ;
///  - la table outbox locale au schéma du module ;
///  - l'isolation par schéma (chaque module passe son nom de schéma).
///
/// Règle d'or : un module ne lit/écrit que dans son propre schéma. Pas de JOIN
/// ni de foreign key cross-schéma — c'est ce qui rend l'extraction mécanique.
/// </summary>
public abstract class ModuleDbContext : DbContext, IUnitOfWork, IOutboxDbContext
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly IntegrationEventQueue _integrationEventQueue;

    protected ModuleDbContext(
        DbContextOptions options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options)
    {
        _domainEventDispatcher = domainEventDispatcher;
        _integrationEventQueue = integrationEventQueue;
    }

    /// <summary>Nom du schéma PostgreSQL propre au module (ex : « catalog »).</summary>
    protected abstract string Schema { get; }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new OutboxConfiguration());
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 1. Dispatch des domain events : les handlers peuvent mettre des
        //    integration events en file.
        await DispatchDomainEventsAsync(cancellationToken);

        // 2. Draine la file vers l'outbox LOCAL (ce DbContext, ce schéma), de
        //    sorte que l'event et le changement d'état soient persistés ensemble.
        DrainIntegrationEventsToOutbox();

        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        var aggregates = ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        var domainEvents = aggregates
            .SelectMany(aggregate => aggregate.DomainEvents)
            .ToList();

        aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());

        await _domainEventDispatcher.DispatchAsync(domainEvents, cancellationToken);
    }

    private void DrainIntegrationEventsToOutbox()
    {
        foreach (var integrationEvent in _integrationEventQueue.DequeueAll())
        {
            OutboxMessages.Add(new OutboxMessage
            {
                Type = EventTypeName.Of(integrationEvent.GetType()),
                Content = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), SerializerOptions),
                OccurredOnUtc = integrationEvent.OccurredOnUtc
            });
        }
    }
}
