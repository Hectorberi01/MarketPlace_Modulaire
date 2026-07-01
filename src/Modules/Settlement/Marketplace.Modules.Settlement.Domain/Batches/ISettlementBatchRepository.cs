namespace Marketplace.Modules.Settlement.Domain.Batches;

public interface ISettlementBatchRepository
{
    Task AddAsync(SettlementBatch batch, CancellationToken cancellationToken = default);

    Task<SettlementBatch?> GetByIdAsync(SettlementBatchId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettlementBatch>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Liste les reversements (payouts) d'un vendeur, tous lots confondus.</summary>
    Task<IReadOnlyList<Payout>> ListPayoutsBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);
}
