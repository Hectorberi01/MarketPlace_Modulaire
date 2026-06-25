namespace Marketplace.Modules.Cart.Domain.Carts;

public interface ICartRepository
{
    Task AddAsync(Cart cart, CancellationToken cancellationToken = default);

    Task<Cart?> GetByIdAsync(CartId id, CancellationToken cancellationToken = default);

    /// <summary>Panier actif de l'acheteur (un seul à la fois).</summary>
    Task<Cart?> GetActiveByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default);
}
