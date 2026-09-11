using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Notifications.Domain.Notifications;

namespace Marketplace.Modules.Notifications.Infrastructure.Persistence;

internal sealed class NotificationRepository : INotificationRepository
{
    private readonly NotificationsDbContext _dbContext;

    public NotificationRepository(NotificationsDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
        => await _dbContext.Notifications.AddAsync(notification, cancellationToken);

    public void Remove(Notification notification) => _dbContext.Notifications.Remove(notification);

    public async Task<Notification?> GetByIdAsync(NotificationId id, CancellationToken cancellationToken = default)
        => await _dbContext.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public async Task<bool> ExistsByDeduplicationKeyAsync(string deduplicationKey, CancellationToken cancellationToken = default)
        => await _dbContext.Notifications
            .AsNoTracking()
            .AnyAsync(n => n.DeduplicationKey == deduplicationKey, cancellationToken);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE DÉCALAGE MANQUAIT, ET SANS LUI LA 51ᵉ NOTIFICATION ÉTAIT INATTEIGNABLE.
    ///
    /// Il n'y avait que `Take`. Un vendeur qui revient après deux semaines avec 130
    /// non lues voyait un badge à 130 — correct, il vient d'un COUNT — et une liste
    /// d'au plus 50. Le seul recours offert par l'écran était « Tout marquer comme
    /// lu », qui les effaçait sans les avoir montrées.
    ///
    /// ON TRIE SUR (DATE, ID). Une pagination par décalage sur une clé non unique
    /// n'est pas stable : deux notifications créées dans la même milliseconde — ce
    /// qu'un même événement produit régulièrement — peuvent s'ordonner autrement d'une
    /// page à l'autre, et l'une apparaît deux fois pendant que l'autre disparaît. Le
    /// départage par identifiant rend l'ordre total, donc le découpage reproductible.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<IReadOnlyList<Notification>> ListByRecipientAsync(Guid recipientUserId, int take, int skip, CancellationToken cancellationToken = default)
        => await _dbContext.Notifications
            .Where(n => n.RecipientUserId == recipientUserId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .ThenByDescending(n => n.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountUnreadAsync(Guid recipientUserId, CancellationToken cancellationToken = default)
        => await _dbContext.Notifications
            .CountAsync(n => n.RecipientUserId == recipientUserId && n.Status != NotificationStatus.Read, cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListUnreadAsync(Guid recipientUserId, CancellationToken cancellationToken = default)
        => await _dbContext.Notifications
            .Where(n => n.RecipientUserId == recipientUserId && n.Status != NotificationStatus.Read)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default)
        => await _dbContext.Notifications
            .AsNoTracking()
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
}
