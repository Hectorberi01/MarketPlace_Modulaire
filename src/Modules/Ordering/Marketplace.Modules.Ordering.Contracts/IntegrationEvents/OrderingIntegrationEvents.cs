using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Ordering.Contracts.IntegrationEvents;

/// <summary>Commande placée (stock réservé, en attente de paiement). Consommé par Cart (clôture) / Payments.</summary>
public sealed record OrderPlacedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid CartId { get; init; }
    public required decimal GrandTotal { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Commande confirmée (paiement encaissé). Consommé par Shipping / Notifications.</summary>
public sealed record OrderConfirmedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
}

/// <summary>Commande annulée. Consommé par Notifications / analytics.</summary>
public sealed record OrderCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Commande livrée. Consommé par Payments (libération escrow) et Settlement (payout).</summary>
public sealed record OrderDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
}
