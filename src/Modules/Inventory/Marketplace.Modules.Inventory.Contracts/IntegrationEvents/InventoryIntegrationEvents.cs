using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Inventory.Contracts.IntegrationEvents;

/// <summary>Du stock a été réservé pour une commande (consommé par Ordering).</summary>
public sealed record StockReservedIntegrationEvent : IntegrationEvent
{
    public required Guid InventoryItemId { get; init; }
    public required string Sku { get; init; }
    public required Guid OrderId { get; init; }
    public required int Quantity { get; init; }
}

/// <summary>Un SKU est en rupture (consommé par Offers pour passer l'offre OutOfStock, Search…).</summary>
public sealed record StockDepletedIntegrationEvent : IntegrationEvent
{
    public required Guid InventoryItemId { get; init; }
    public required string Sku { get; init; }
    public required Guid LocationId { get; init; }
}
