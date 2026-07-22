using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Shipping.Domain.Carriers;

namespace Marketplace.Modules.Shipping.Infrastructure.Persistence;

internal sealed class CarrierRepository : ICarrierRepository
{
    private readonly ShippingDbContext _dbContext;

    public CarrierRepository(ShippingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Carrier carrier, CancellationToken cancellationToken = default)
        => await _dbContext.Carriers.AddAsync(carrier, cancellationToken);

    public async Task<Carrier?> GetByIdAsync(CarrierId id, CancellationToken cancellationToken = default)
        => await _dbContext.Carriers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<Carrier?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        return await _dbContext.Carriers.FirstOrDefaultAsync(c => c.Code == normalized, cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        return await _dbContext.Carriers.AnyAsync(c => c.Code == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<Carrier>> ListAllAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Carriers.OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Carrier>> ListActiveAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Carriers.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(cancellationToken);
}
