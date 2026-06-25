namespace Marketplace.Modules.Ordering.Domain.Orders;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default);
}
