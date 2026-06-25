using System.Text.Json;
using Marketplace.BuildingBlocks.Infrastructure.Serialization;
using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Publisher d'events d'intégration via l'Outbox. « Publier » = écrire une ligne
/// dans la table outbox du module, dans la même unité de travail que le
/// changement d'état. Aucun appel direct à un autre module : la livraison réelle
/// est faite plus tard par <see cref="OutboxProcessor{TDbContext}"/>.
/// </summary>
public sealed class OutboxIntegrationEventPublisher : IIntegrationEventPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IOutboxDbContext _dbContext;

    public OutboxIntegrationEventPublisher(IOutboxDbContext dbContext)
        => _dbContext = dbContext;

    public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var message = new OutboxMessage
        {
            Type = EventTypeName.Of(integrationEvent.GetType()),
            Content = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), SerializerOptions),
            OccurredOnUtc = integrationEvent.OccurredOnUtc
        };

        _dbContext.OutboxMessages.Add(message);

        // On ne SaveChanges pas ici : c'est l'Unit of Work du module qui commitera
        // l'event et le changement d'état atomiquement.
        return Task.CompletedTask;
    }
}
