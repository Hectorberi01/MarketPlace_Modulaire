using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Infrastructure.Persistence;

internal sealed class SellerRepository : ISellerRepository
{
    private readonly SellersDbContext _dbContext;

    public SellerRepository(SellersDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(Seller seller, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers.AddAsync(seller, cancellationToken);

    public void Remove(Seller seller)
        => _dbContext.Sellers.Remove(seller);

    public async Task<Seller?> GetByIdAsync(SellerId id, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers
            .Include(s => s.KybDocuments)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<Seller?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers
            .Include(s => s.KybDocuments)
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

    public async Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers.AnyAsync(s => s.UserId == userId, cancellationToken);

    public async Task<bool> ShopNameExistsAsync(string shopName, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers.AnyAsync(s => s.ShopName == shopName, cancellationToken);

    public async Task<IReadOnlyList<Seller>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Sellers
            .Include(s => s.KybDocuments)
            .OrderBy(s => s.ShopName)
            .ToListAsync(cancellationToken);
}
