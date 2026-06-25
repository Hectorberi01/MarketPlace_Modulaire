namespace Marketplace.Modules.Settlement.Domain.Batches;

public interface ISettlementBatchRepository
{
    Task AddAsync(SettlementBatch batch, CancellationToken cancellationToken = default);

    Task<SettlementBatch?> GetByIdAsync(SettlementBatchId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettlementBatch>> ListAsync(CancellationToken cancellationToken = default);
}
