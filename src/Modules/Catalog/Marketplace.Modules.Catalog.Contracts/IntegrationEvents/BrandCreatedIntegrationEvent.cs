using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Catalog.Contracts.IntegrationEvents;

public sealed record BrandCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid BrandId { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
}
