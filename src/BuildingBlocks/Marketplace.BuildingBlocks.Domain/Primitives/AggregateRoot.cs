using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Aggregate Root : point d'entrée transactionnel et garant des invariants.
/// Seul un agrégat accumule des domain events, collectés puis dispatchés
/// après persistance par l'Unit of Work.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();

    protected AggregateRoot(TId id) : base(id)
    {
    }

    protected AggregateRoot()
    {
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
