using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Returns.Domain.Returns.Events;

/// <summary>Une demande de retour a été ouverte.</summary>
public sealed record ReturnRequestedDomainEvent(Guid ReturnRequestId, Guid OrderId, Guid OfferId, Guid BuyerId) : DomainEvent;

/// <summary>Un retour a été remboursé — impacte l'escrow / le payout vendeur.</summary>
public sealed record ReturnRefundedDomainEvent(Guid ReturnRequestId, Guid OrderId, Guid SellerId, decimal RefundAmount, string Currency) : DomainEvent;
