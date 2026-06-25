namespace Marketplace.Modules.Shipping.Domain.Shipments;

public interface IShipmentRepository
{
    Task AddAsync(Shipment shipment, CancellationToken cancellationToken = default);

    Task<Shipment?> GetByIdAsync(ShipmentId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Shipment>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Shipment>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Vrai si des expéditions existent déjà pour la commande (idempotence).</summary>
    Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}
