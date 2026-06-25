using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// File scopée d'events d'intégration. « Publier » revient à mettre l'event en
/// file ; c'est le DbContext du module (qui sait dans quel schéma écrire) qui
/// draine la file et écrit les lignes d'outbox dans la MÊME transaction que le
/// changement d'état.
///
/// Ce découplage évite que le publisher dépende d'un IOutboxDbContext partagé :
/// en monolithe multi-modules, plusieurs DbContext coexistent et une seule file
/// scopée par requête route les events vers le bon module.
/// </summary>
public sealed class IntegrationEventQueue : IIntegrationEventPublisher
{
    private readonly List<IntegrationEvent> _events = new();

    public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        _events.Add(integrationEvent);
        return Task.CompletedTask;
    }

    /// <summary>Retire et renvoie tous les events en attente.</summary>
    public IReadOnlyList<IntegrationEvent> DequeueAll()
    {
        var drained = _events.ToList();
        _events.Clear();
        return drained;
    }
}
