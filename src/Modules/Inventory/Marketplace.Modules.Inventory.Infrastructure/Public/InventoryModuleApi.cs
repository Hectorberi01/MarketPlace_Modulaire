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

    public async Task<FulfillmentLocationSummary?> GetLocationAsync(
        Guid locationId, CancellationToken cancellationToken = default)
    {
        var location = await _dbContext.FulfillmentLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == new Domain.Locations.FulfillmentLocationId(locationId), cancellationToken);

        if (location is null)
        {
            return null;
        }

        // ⚠️ Latitude et Longitude sont bien RECOPIÉES. La projection jumelle de
        // ListFulfillmentLocationsQuery les écrasait autrefois par « null, null » :
        // une saisie GPS ne survivait pas à sa propre relecture. Une projection
        // écrite à un second endroit est l'occasion parfaite de refaire la même
        // erreur — d'où ce rappel plutôt qu'un simple mapping muet.
        return new FulfillmentLocationSummary(
            location.Id.Value,
            location.Type.ToString(),
            location.OwnerId,
            location.Address.CommuneCode,
            location.Address.CommuneName,
            location.Address.Quartier,
            location.Address.Landmark,
            location.Address.Line,
            location.Address.CountryCode,
            location.Address.Latitude,
            location.Address.Longitude,
            location.Address.ContactPhone);
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
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return false;
        }

        var value = skuResult.Value;
        var items = await _dbContext.InventoryItems
            .AsNoTracking()
            .Include(i => i.Reservations)
            .Where(i => i.Sku == value)
            .ToListAsync(cancellationToken);

        // SKU non suivi en stock (aucun enregistrement Inventory) : considéré
        // disponible à la vente. Dès qu'un stock est défini, la disponibilité
        // réelle est enforced.
        if (items.Count == 0)
        {
            return true;
        }

        return items.Sum(i => i.Available) >= quantity;
    }

    public async Task<bool> TryReserveAsync(string sku, Guid locationId, Guid orderId, int quantity, CancellationToken cancellationToken = default)
    {
        // Cohérent avec IsInStockAsync : un SKU sans enregistrement de stock à
        // cette localisation n'est pas « suivi » → vendable sans réservation. On
        // ne bloque la commande que lorsqu'un stock est réellement défini.
        if (!await HasStockRecordAsync(sku, locationId, cancellationToken))
        {
            return true;
        }

        var result = await _sender.Send(new ReserveStockCommand(sku, locationId, orderId, quantity), cancellationToken);
        return result.IsSuccess;
    }

    public async Task ReleaseReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        if (!await HasStockRecordAsync(sku, locationId, cancellationToken))
        {
            return; // rien à libérer pour un SKU non suivi
        }

        await _sender.Send(new ReleaseReservationCommand(sku, locationId, orderId), cancellationToken);
    }

    public async Task<bool> ConfirmReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        if (!await HasStockRecordAsync(sku, locationId, cancellationToken))
        {
            return true; // SKU non suivi : confirmation sans décrément
        }

        var result = await _sender.Send(new ConfirmReservationCommand(sku, locationId, orderId), cancellationToken);
        return result.IsSuccess;
    }

    /// <summary>Existe-t-il un enregistrement de stock pour ce SKU à cette localisation ?</summary>
    private async Task<bool> HasStockRecordAsync(string sku, Guid locationId, CancellationToken cancellationToken)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return false;
        }

        var value = skuResult.Value;
        return await _dbContext.InventoryItems
            .AsNoTracking()
            .AnyAsync(i => i.Sku == value && i.LocationId == locationId, cancellationToken);
    }
}
