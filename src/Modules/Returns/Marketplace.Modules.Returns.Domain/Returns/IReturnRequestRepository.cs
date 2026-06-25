namespace Marketplace.Modules.Returns.Domain.Returns;

public interface IReturnRequestRepository
{
    Task AddAsync(ReturnRequest request, CancellationToken cancellationToken = default);

    Task<ReturnRequest?> GetByIdAsync(ReturnRequestId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReturnRequest>> ListByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReturnRequest>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Un seul retour ouvert par ligne de commande.</summary>
    Task<bool> ExistsOpenAsync(Guid orderId, Guid offerId, CancellationToken cancellationToken = default);
}
