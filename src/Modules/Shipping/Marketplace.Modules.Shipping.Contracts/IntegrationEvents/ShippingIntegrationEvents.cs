using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Shipping.Contracts.IntegrationEvents;

/// <summary>Expédition remise au transporteur. Consommé par Notifications.</summary>
public sealed record ShipmentShippedIntegrationEvent : IntegrationEvent
{
    public required Guid ShipmentId { get; init; }
    public required Guid OrderId { get; init; }
    public required string Carrier { get; init; }
    public required string TrackingNumber { get; init; }
}

/// <summary>Expédition livrée. Consommé par Notifications / Reviews / Settlement (payout vendeur).</summary>
public sealed record ShipmentDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid ShipmentId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid SellerId { get; init; }
}
