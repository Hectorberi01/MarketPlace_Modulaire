namespace Marketplace.BuildingBlocks.Domain.Events;

/// <summary>
/// Event de domaine : un fait métier qui s'est produit DANS un module.
/// Marqueur pur (pas de dépendance MediatR ici) pour garder le Domain isolé.
/// La traduction vers un IntegrationEvent (publié sur le bus) se fait en
/// Infrastructure, jamais dans le Domain.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredOnUtc { get; }
}

/// <summary>Base pratique pour les events de domaine.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
