using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Disputes.Domain.Disputes.Events;

/// <summary>Un litige a été ouvert — l'escrow de la commande doit être gelé.</summary>
public sealed record DisputeOpenedDomainEvent(Guid DisputeId, Guid OrderId, Guid RaisedBy, string Type) : DomainEvent;

/// <summary>Un litige a été tranché — l'escrow est libéré selon la décision.
/// <paramref name="RefundAmount"/> est renseigné pour un remboursement partiel.</summary>
public sealed record DisputeResolvedDomainEvent(Guid DisputeId, Guid OrderId, string Resolution, decimal? RefundAmount) : DomainEvent;
