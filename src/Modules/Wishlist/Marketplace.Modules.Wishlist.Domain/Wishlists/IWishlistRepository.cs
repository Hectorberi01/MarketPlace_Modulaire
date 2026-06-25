namespace Marketplace.Modules.Wishlist.Domain.Wishlists;

public interface IWishlistRepository
{
    Task AddAsync(Wishlist wishlist, CancellationToken cancellationToken = default);

    Task<Wishlist?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
