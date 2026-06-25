using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Cart.Domain.Carts;
using CartAggregate = Marketplace.Modules.Cart.Domain.Carts.Cart;

namespace Marketplace.Modules.Cart.Infrastructure.Persistence;

internal sealed class CartRepository : ICartRepository
{
    private readonly CartDbContext _dbContext;

    public CartRepository(CartDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(CartAggregate cart, CancellationToken cancellationToken = default)
        => await _dbContext.Carts.AddAsync(cart, cancellationToken);

    public async Task<CartAggregate?> GetByIdAsync(CartId id, CancellationToken cancellationToken = default)
        => await _dbContext.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<CartAggregate?> GetActiveByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default)
        => await _dbContext.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.BuyerId == buyerId && c.Status == CartStatus.Active, cancellationToken);
}
