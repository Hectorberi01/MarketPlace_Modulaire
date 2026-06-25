using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Inventory.Domain.Stock;

namespace Marketplace.Modules.Inventory.Application.Stock;

internal static class InventoryMapper
{
    public static InventoryItemSummary ToSummary(InventoryItem item) => new(
        item.Id.Value,
        item.Sku.Value,
        item.LocationId,
        item.OnHand,
        item.Reserved,
        item.Available,
        item.ReorderThreshold,
        item.IsLowStock);
}
