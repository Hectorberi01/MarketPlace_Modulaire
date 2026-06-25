using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Inventory.Domain.Locations;

namespace Marketplace.Modules.Inventory.Infrastructure.Persistence;

internal sealed class FulfillmentLocationRepository : IFulfillmentLocationRepository
{
    private readonly InventoryDbContext _dbContext;

    public FulfillmentLocationRepository(InventoryDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(FulfillmentLocation location, CancellationToken cancellationToken = default)
        => await _dbContext.FulfillmentLocations.AddAsync(location, cancellationToken);

    public async Task<FulfillmentLocation?> GetByIdAsync(FulfillmentLocationId id, CancellationToken cancellationToken = default)
        => await _dbContext.FulfillmentLocations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<IReadOnlyList<FulfillmentLocation>> ListByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => await _dbContext.FulfillmentLocations.Where(l => l.OwnerId == ownerId).ToListAsync(cancellationToken);
}
