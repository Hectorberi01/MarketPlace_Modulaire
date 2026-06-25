using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Ordering.Domain.Orders.Events;

/// <summary>Une commande a été placée (stock réservé, en attente de paiement).</summary>
public sealed record OrderPlacedDomainEvent(Guid OrderId, Guid BuyerId, Guid CartId, decimal GrandTotal, string Currency) : DomainEvent;

/// <summary>La commande a été confirmée (paiement encaissé, stock soldé).</summary>
public sealed record OrderConfirmedDomainEvent(Guid OrderId, Guid BuyerId) : DomainEvent;

/// <summary>La commande a été annulée (réservations libérées).</summary>
public sealed record OrderCancelledDomainEvent(Guid OrderId, Guid BuyerId, string Reason) : DomainEvent;

/// <summary>La commande a été livrée (escrow à libérer, payout vendeur à déclencher).</summary>
public sealed record OrderDeliveredDomainEvent(Guid OrderId, Guid BuyerId) : DomainEvent;
