using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Pricing.Contracts.IntegrationEvents;

/// <summary>Une promotion a été créée (consommée par Marketing/CMS, Search…).</summary>
public sealed record PromotionCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid PromotionId { get; init; }
    public required string Type { get; init; }
    public required string ScopeType { get; init; }
}
