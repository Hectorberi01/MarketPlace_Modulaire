using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Disputes.Domain.Disputes;

namespace Marketplace.Modules.Disputes.Infrastructure.Persistence;

internal sealed class DisputeRepository : IDisputeRepository
{
    private readonly DisputesDbContext _dbContext;

    public DisputeRepository(DisputesDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Dispute dispute, CancellationToken cancellationToken = default)
        => await _dbContext.Disputes.AddAsync(dispute, cancellationToken);

    public async Task<Dispute?> GetByIdAsync(DisputeId id, CancellationToken cancellationToken = default)
        => await _dbContext.Disputes.Include(d => d.Messages).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Dispute>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Disputes
            .Include(d => d.Messages)
            .Where(d => d.OrderId == orderId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Dispute>> ListByParticipantAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Disputes
            .Include(d => d.Messages)
            .Where(d => d.RaisedBy == userId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}
