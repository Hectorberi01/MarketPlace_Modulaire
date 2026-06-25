namespace Marketplace.Modules.Inventory.Domain.Stock;

public interface IInventoryItemRepository
{
    Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default);

    Task<InventoryItem?> GetByIdAsync(InventoryItemId id, CancellationToken cancellationToken = default);

    Task<InventoryItem?> GetBySkuAndLocationAsync(string sku, Guid locationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryItem>> ListBySkuAsync(string sku, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryItem>> ListLowStockAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string sku, Guid locationId, CancellationToken cancellationToken = default);
}
