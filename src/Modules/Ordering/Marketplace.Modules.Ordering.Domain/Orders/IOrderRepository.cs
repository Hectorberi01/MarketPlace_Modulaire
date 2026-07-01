namespace Marketplace.Modules.Ordering.Domain.Orders;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default);

    /// <summary>Commandes comportant au moins une ligne vendue par ce vendeur (back-office vendeur).</summary>
    Task<IReadOnlyList<Order>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Toutes les commandes de la plateforme (back-office admin).</summary>
    Task<IReadOnlyList<Order>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default);
}
