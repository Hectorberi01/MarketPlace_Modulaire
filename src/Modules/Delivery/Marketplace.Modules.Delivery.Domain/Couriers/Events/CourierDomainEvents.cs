using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Delivery.Domain.Couriers.Events;

/// <summary>Un coursier vient d'être enregistré (vérification à faire).</summary>
public sealed record CourierRegisteredDomainEvent(Guid CourierId, string DisplayName) : DomainEvent;

/// <summary>Un coursier a été vérifié : il peut désormais être payé.</summary>
public sealed record CourierActivatedDomainEvent(Guid CourierId) : DomainEvent;

/// <summary>Un coursier a été suspendu (motif à l'appui).</summary>
public sealed record CourierSuspendedDomainEvent(Guid CourierId, string Reason) : DomainEvent;
