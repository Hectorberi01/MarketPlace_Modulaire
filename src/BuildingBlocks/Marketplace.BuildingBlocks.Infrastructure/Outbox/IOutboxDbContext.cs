using Microsoft.EntityFrameworkCore;

namespace Marketplace.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Implémenté par chaque DbContext de module : expose sa table d'outbox (dans
/// son propre schéma) afin que le publisher et le processeur soient génériques.
/// </summary>
public interface IOutboxDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
