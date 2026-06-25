namespace Marketplace.BuildingBlocks.IntegrationEvents;

/// <summary>
/// Event d'intégration : un fait publié sur le bus, consommé par d'autres
/// modules de façon asynchrone et découplée. C'est le contrat public ; il ne
/// doit jamais exposer d'entité interne, uniquement des primitives stables.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Handler d'un event d'intégration côté consommateur.</summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publication d'un event d'intégration. L'implémentation passe par l'Outbox
/// (in-process aujourd'hui, Kafka demain) : on n'appelle jamais un autre module
/// directement pour un event.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
