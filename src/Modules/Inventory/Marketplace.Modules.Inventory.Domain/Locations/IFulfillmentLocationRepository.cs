namespace Marketplace.Modules.Inventory.Domain.Locations;

public interface IFulfillmentLocationRepository
{
    Task AddAsync(FulfillmentLocation location, CancellationToken cancellationToken = default);

    void Remove(FulfillmentLocation location);

    Task<FulfillmentLocation?> GetByIdAsync(FulfillmentLocationId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FulfillmentLocation>> ListByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>Tous les lieux d'expédition de la plateforme (back-office admin).</summary>
    Task<IReadOnlyList<FulfillmentLocation>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default);
}
