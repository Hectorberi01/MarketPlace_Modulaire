using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Settlement.Contracts.IntegrationEvents;

/// <summary>Un reversement a été versé à un vendeur. Consommé par Notifications / comptabilité.</summary>
public sealed record PayoutPaidIntegrationEvent : IntegrationEvent
{
    public required Guid BatchId { get; init; }
    public required Guid PayoutId { get; init; }
    public required Guid SellerId { get; init; }
    public required decimal NetAmount { get; init; }
    public required string Currency { get; init; }
}
