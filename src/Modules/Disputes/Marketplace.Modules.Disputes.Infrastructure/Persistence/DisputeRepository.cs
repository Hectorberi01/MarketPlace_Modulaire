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

    public async Task<IReadOnlyList<Dispute>> ListByOrdersAsync(
        IReadOnlyCollection<Guid> orderIds, int take = 500, CancellationToken cancellationToken = default)
    {
        // Un IN () vide est une requête inutile — et selon le fournisseur, une syntaxe
        // invalide. L'appelant qui n'a aucune commande n'a aucun litige.
        if (orderIds.Count == 0)
        {
            return Array.Empty<Dispute>();
        }

        // `Contains` se traduit en `WHERE "OrderId" = ANY(...)` sous Npgsql : le filtre
        // s'exécute en base, donc `Take` porte bien sur les litiges retenus.
        return await _dbContext.Disputes
            .AsNoTracking()
            .Include(d => d.Messages)
            .Where(d => orderIds.Contains(d.OrderId))
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Dispute>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default)
        => await _dbContext.Disputes
            .AsNoTracking()
            .Include(d => d.Messages)
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
}
