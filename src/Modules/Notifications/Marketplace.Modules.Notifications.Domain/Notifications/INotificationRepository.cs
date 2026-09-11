namespace Marketplace.Modules.Notifications.Domain.Notifications;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);

    void Remove(Notification notification);

    Task<Notification?> GetByIdAsync(NotificationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Une notification portant cette clé d'unicité existe-t-elle déjà ?
    ///
    /// Chemin rapide de l'idempotence : il évite d'écrire, de pousser et d'envoyer un
    /// e-mail pour un fait déjà notifié. Il ne SUFFIT pas — deux tentatives concurrentes
    /// liraient toutes deux « non » — et c'est l'index unique partiel sur
    /// <see cref="Notification.DeduplicationKey"/> qui tranche alors la course.
    /// </summary>
    Task<bool> ExistsByDeduplicationKeyAsync(string deduplicationKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Page de notifications d'un destinataire, les plus récentes d'abord.
    /// [skip] est le nombre d'entrées à sauter — sans lui, rien au-delà de la première
    /// page n'était atteignable. Voir l'implémentation pour la stabilité du tri.
    /// </summary>
    Task<IReadOnlyList<Notification>> ListByRecipientAsync(Guid recipientUserId, int take, int skip, CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(Guid recipientUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> ListUnreadAsync(Guid recipientUserId, CancellationToken cancellationToken = default);

    /// <summary>Toutes les notifications de la plateforme (back-office admin).</summary>
    Task<IReadOnlyList<Notification>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default);
}
