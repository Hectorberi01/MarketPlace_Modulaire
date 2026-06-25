using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Reviews.Domain.Reviews.Events;

/// <summary>Un avis a été publié — Search met à jour la note du produit.</summary>
public sealed record ReviewPublishedDomainEvent(Guid ReviewId, Guid ProductId, int Rating) : DomainEvent;

/// <summary>Un avis a été retiré (rejeté) — Search retire sa contribution à la note.</summary>
public sealed record ReviewRejectedDomainEvent(Guid ReviewId, Guid ProductId) : DomainEvent;
