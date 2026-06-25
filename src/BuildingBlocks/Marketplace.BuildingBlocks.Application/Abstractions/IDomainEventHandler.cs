using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Handler d'un domain event, à l'INTÉRIEUR du module. C'est typiquement ici
/// qu'on traduit un fait de domaine en IntegrationEvent publié sur le bus.
/// </summary>
public interface IDomainEventHandler<in TDomainEvent> where TDomainEvent : IDomainEvent
{
    Task HandleAsync(TDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Dispatche les domain events collectés sur les agrégats après persistance.
/// Implémenté en Infrastructure (résolution des handlers via le conteneur).
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
