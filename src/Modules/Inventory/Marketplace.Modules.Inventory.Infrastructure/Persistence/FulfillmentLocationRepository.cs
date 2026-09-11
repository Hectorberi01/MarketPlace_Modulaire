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

    public void Remove(FulfillmentLocation location) => _dbContext.FulfillmentLocations.Remove(location);

    public async Task<FulfillmentLocation?> GetByIdAsync(FulfillmentLocationId id, CancellationToken cancellationToken = default)
        => await _dbContext.FulfillmentLocations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<IReadOnlyList<FulfillmentLocation>> ListByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => await _dbContext.FulfillmentLocations.Where(l => l.OwnerId == ownerId).ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<FulfillmentLocation> Items, int Total)> ListAllAsync(
        int take = 500, CancellationToken cancellationToken = default)
    {
        // Le compte est fait sur la table entière, la page est bornée. Deux requêtes
        // plutôt qu'une, et c'est le prix à payer pour pouvoir dire « 500 sur 1 240 » au
        // lieu de laisser un écran déduire d'une liste pleine qu'elle est complète.
        var total = await _dbContext.FulfillmentLocations.CountAsync(cancellationToken);

        var items = await _dbContext.FulfillmentLocations
            .AsNoTracking()
            .OrderByDescending(l => l.CreatedOnUtc)
            // Départage : PostgreSQL ne garantit aucun ordre entre deux lignes créées
            // dans la même transaction, et sans lui la borne coupe à un endroit qui
            // change d'une requête à l'autre.
            .ThenBy(l => l.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
