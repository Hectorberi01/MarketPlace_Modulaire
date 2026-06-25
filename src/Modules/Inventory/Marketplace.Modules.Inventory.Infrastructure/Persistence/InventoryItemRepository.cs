using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Inventory.Domain.Common;
using Marketplace.Modules.Inventory.Domain.Stock;

namespace Marketplace.Modules.Inventory.Infrastructure.Persistence;

internal sealed class InventoryItemRepository : IInventoryItemRepository
{
    private readonly InventoryDbContext _dbContext;

    public InventoryItemRepository(InventoryDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default)
        => await _dbContext.InventoryItems.AddAsync(item, cancellationToken);

    public async Task<InventoryItem?> GetByIdAsync(InventoryItemId id, CancellationToken cancellationToken = default)
        => await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<InventoryItem?> GetBySkuAndLocationAsync(string sku, Guid locationId, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return null;
        }

        var value = skuResult.Value;
        return await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .FirstOrDefaultAsync(i => i.Sku == value && i.LocationId == locationId, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryItem>> ListBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return Array.Empty<InventoryItem>();
        }

        var value = skuResult.Value;
        return await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .Where(i => i.Sku == value)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryItem>> ListLowStockAsync(CancellationToken cancellationToken = default)
    {
        // Available dépend des réservations (calculé en mémoire) : on charge puis on filtre.
        var items = await _dbContext.InventoryItems.Include(i => i.Reservations).ToListAsync(cancellationToken);
        return items.Where(i => i.IsLowStock).ToList();
    }

    public async Task<bool> ExistsAsync(string sku, Guid locationId, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return false;
        }

        var value = skuResult.Value;
        return await _dbContext.InventoryItems.AnyAsync(i => i.Sku == value && i.LocationId == locationId, cancellationToken);
    }
}
