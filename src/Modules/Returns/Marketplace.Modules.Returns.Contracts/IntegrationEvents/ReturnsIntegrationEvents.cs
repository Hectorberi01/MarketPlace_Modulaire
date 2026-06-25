using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Returns.Contracts.IntegrationEvents;

/// <summary>Un retour a été remboursé. Consommé par Settlement (reprise du payout) / Notifications.</summary>
public sealed record ReturnRefundedIntegrationEvent : IntegrationEvent
{
    public required Guid ReturnRequestId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid SellerId { get; init; }
    public required decimal RefundAmount { get; init; }
    public required string Currency { get; init; }
}
