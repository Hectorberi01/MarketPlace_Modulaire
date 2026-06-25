using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Catalog.Contracts.IntegrationEvents;

/// <summary>
/// Publié sur le bus quand un produit est créé. Consommé par Search (indexation),
/// et potentiellement Inventory, Recommendations… de façon découplée.
/// </summary>
public sealed record ProductCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required Guid CategoryId { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
}
