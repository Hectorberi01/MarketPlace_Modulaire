namespace Marketplace.Modules.Disputes.Domain.Disputes;

public interface IDisputeRepository
{
    Task AddAsync(Dispute dispute, CancellationToken cancellationToken = default);

    Task<Dispute?> GetByIdAsync(DisputeId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Dispute>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Dispute>> ListByParticipantAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Tous les litiges de la plateforme (back-office admin / modération).</summary>
    Task<IReadOnlyList<Dispute>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default);
}
