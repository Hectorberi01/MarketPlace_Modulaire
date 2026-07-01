using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Identity.Domain.Addresses;

namespace Marketplace.Modules.Identity.Infrastructure.Persistence;

internal sealed class AddressRepository : IAddressRepository
{
    private readonly IdentityDbContext _dbContext;

    public AddressRepository(IdentityDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Address address, CancellationToken cancellationToken = default)
        => await _dbContext.Addresses.AddAsync(address, cancellationToken);

    public void Remove(Address address)
        => _dbContext.Addresses.Remove(address);

    public async Task<Address?> GetByIdAsync(AddressId id, CancellationToken cancellationToken = default)
        => await _dbContext.Addresses.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Address>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Addresses
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedOnUtc)
            .ToListAsync(cancellationToken);
}
