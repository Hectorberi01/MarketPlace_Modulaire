using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Delivery.Domain.Couriers;

namespace Marketplace.Modules.Delivery.Infrastructure.Persistence;

internal sealed class CourierRepository : ICourierRepository
{
    private readonly DeliveryDbContext _dbContext;

    public CourierRepository(DeliveryDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Courier courier, CancellationToken cancellationToken = default)
        => await _dbContext.Couriers.AddAsync(courier, cancellationToken);

    // Suivi EF activé : un coursier chargé par son identifiant l'est pour être muté
    // (activation, suspension, changement de compte de versement).
    public async Task<Courier?> GetByIdAsync(CourierId id, CancellationToken cancellationToken = default)
        => await _dbContext.Couriers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<Courier?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Couriers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public async Task<Courier?> GetByExternalRefAsync(string externalRef, CancellationToken cancellationToken = default)
        => await _dbContext.Couriers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ExternalRef == externalRef, cancellationToken);

    // Un EXISTS servi par l'index unique, sans charger l'agrégat.
    public async Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Couriers.AnyAsync(c => c.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Courier>> ListAsync(
        CourierStatus? status = null, CancellationToken cancellationToken = default)
        => await _dbContext.Couriers
            .AsNoTracking()
            .Where(c => status == null || c.Status == status)
            .OrderBy(c => c.DisplayName)
            .ToListAsync(cancellationToken);
}
