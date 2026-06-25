namespace Marketplace.Modules.Inventory.Domain.Locations;

public interface IFulfillmentLocationRepository
{
    Task AddAsync(FulfillmentLocation location, CancellationToken cancellationToken = default);

    Task<FulfillmentLocation?> GetByIdAsync(FulfillmentLocationId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FulfillmentLocation>> ListByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default);
}
