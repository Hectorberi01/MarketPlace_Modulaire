using MediatR;
using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Inventory.Application.Stock.Commands;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Inventory.Domain.Common;
using Marketplace.Modules.Inventory.Infrastructure.Persistence;

namespace Marketplace.Modules.Inventory.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Inventory.</summary>
internal sealed class InventoryModuleApi : IInventoryModuleApi
{
    private readonly InventoryDbContext _dbContext;
    private readonly ISender _sender;

    public InventoryModuleApi(InventoryDbContext dbContext, ISender sender)
    {
        _dbContext = dbContext;
        _sender = sender;
    }

    public async Task<AvailabilitySummary> GetAvailabilityAsync(string sku, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return new AvailabilitySummary(sku, 0);
        }

        var value = skuResult.Value;
        var items = await _dbContext.InventoryItems
            .AsNoTracking()
            .Include(i => i.Reservations)
            .Where(i => i.Sku == value)
            .ToListAsync(cancellationToken);

        return new AvailabilitySummary(value.Value, items.Sum(i => i.Available));
    }

    public async Task<bool> IsInStockAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        var availability = await GetAvailabilityAsync(sku, cancellationToken);
        return availability.TotalAvailable >= quantity;
    }

    public async Task<bool> TryReserveAsync(string sku, Guid locationId, Guid orderId, int quantity, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new ReserveStockCommand(sku, locationId, orderId, quantity), cancellationToken);
        return result.IsSuccess;
    }

    public async Task ReleaseReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
        => await _sender.Send(new ReleaseReservationCommand(sku, locationId, orderId), cancellationToken);

    public async Task<bool> ConfirmReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new ConfirmReservationCommand(sku, locationId, orderId), cancellationToken);
        return result.IsSuccess;
    }
}
