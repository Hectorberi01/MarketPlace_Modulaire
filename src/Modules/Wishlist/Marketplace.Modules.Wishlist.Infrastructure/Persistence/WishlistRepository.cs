using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Wishlist.Domain.Wishlists;
using WishlistAggregate = Marketplace.Modules.Wishlist.Domain.Wishlists.Wishlist;

namespace Marketplace.Modules.Wishlist.Infrastructure.Persistence;

internal sealed class WishlistRepository : IWishlistRepository
{
    private readonly WishlistDbContext _dbContext;

    public WishlistRepository(WishlistDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(WishlistAggregate wishlist, CancellationToken cancellationToken = default)
        => await _dbContext.Wishlists.AddAsync(wishlist, cancellationToken);

    public async Task<WishlistAggregate?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Wishlists
            .Include(w => w.Items)
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);
}
