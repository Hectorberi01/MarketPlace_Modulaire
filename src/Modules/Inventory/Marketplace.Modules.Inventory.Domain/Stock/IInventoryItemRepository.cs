namespace Marketplace.Modules.Inventory.Domain.Stock;

public interface IInventoryItemRepository
{
    Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default);

    Task<InventoryItem?> GetByIdAsync(InventoryItemId id, CancellationToken cancellationToken = default);

    Task<InventoryItem?> GetBySkuAndLocationAsync(string sku, Guid locationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryItem>> ListBySkuAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Articles de stock situés dans un ensemble de localisations.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Ce module ne connaît pas la notion de vendeur : un article appartient à une
    /// LOCALISATION, et c'est la localisation qui porte un `OwnerId`. Pour lister le
    /// stock d'une boutique, l'appelant résout donc d'abord ses localisations, puis
    /// interroge cette méthode.
    ///
    /// Sans elle, la seule voie était `ListBySkuAsync` appelée une fois par référence
    /// du catalogue — autant de requêtes que de SKU à chaque affichage de l'écran
    /// Stock, pour reconstituer une liste que la base sait produire d'un coup.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    Task<IReadOnlyList<InventoryItem>> ListByLocationsAsync(
        IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryItem>> ListLowStockAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string sku, Guid locationId, CancellationToken cancellationToken = default);
}
