using System.Collections.Concurrent;
using Marketplace.Modules.Inventory.Contracts;

namespace Saga.IntegrationTests;

/// <summary>
/// Substitut in-memory de l'API Inventory : isole la saga Ordering du module
/// Inventory réel tout en traçant les appels de réservation/confirmation/
/// libération pour les assertions du test.
/// </summary>
public sealed class FakeInventoryModuleApi : IInventoryModuleApi
{
    public ConcurrentBag<(string Sku, Guid OrderId)> Confirmed { get; } = new();
    public ConcurrentBag<(string Sku, Guid OrderId)> Released { get; } = new();

    public Task<AvailabilitySummary> GetAvailabilityAsync(string sku, CancellationToken cancellationToken = default)
        => Task.FromResult(new AvailabilitySummary(sku, 100));

    public Task<bool> IsInStockAsync(string sku, int quantity, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> TryReserveAsync(string sku, Guid locationId, Guid orderId, int quantity, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task ReleaseReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        Released.Add((sku, orderId));
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        Confirmed.Add((sku, orderId));
        return Task.FromResult(true);
    }

    /// <summary>Lieu fictif : adresse plausible de Cotonou, avec le point de repère qui compte.</summary>
    public Task<FulfillmentLocationSummary?> GetLocationAsync(Guid locationId, CancellationToken cancellationToken = default)
        => Task.FromResult<FulfillmentLocationSummary?>(new FulfillmentLocationSummary(
            locationId, "Warehouse", null,
            "cotonou", "Cotonou", "Gbedjromede", "En face de la pharmacie", null, "BJ", null, null));
}
