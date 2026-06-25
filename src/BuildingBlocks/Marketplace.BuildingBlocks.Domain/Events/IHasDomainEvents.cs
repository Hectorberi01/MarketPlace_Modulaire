namespace Marketplace.BuildingBlocks.Domain.Events;

/// <summary>
/// Marqueur non générique permettant à l'infrastructure (DbContext) de
/// retrouver les agrégats porteurs d'events dans le ChangeTracker, sans
/// connaître leur type d'Id.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
