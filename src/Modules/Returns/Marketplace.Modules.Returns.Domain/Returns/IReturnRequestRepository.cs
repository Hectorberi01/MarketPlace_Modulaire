namespace Marketplace.Modules.Returns.Domain.Returns;

public interface IReturnRequestRepository
{
    Task AddAsync(ReturnRequest request, CancellationToken cancellationToken = default);

    Task<ReturnRequest?> GetByIdAsync(ReturnRequestId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReturnRequest>> ListByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReturnRequest>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retours REMBOURSÉS d'un vendeur dont la date de résolution (remboursement)
    /// tombe dans [fromUtc, toUtc). Sert au relevé finances (lignes « refund »).
    /// </summary>
    Task<IReadOnlyList<ReturnRequest>> ListRefundedBySellerAsync(
        Guid sellerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>Un seul retour ouvert par ligne de commande.</summary>
    Task<bool> ExistsOpenAsync(Guid orderId, Guid offerId, CancellationToken cancellationToken = default);
}
