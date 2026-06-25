namespace Marketplace.Modules.Notifications.Domain.Notifications;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);

    Task<Notification?> GetByIdAsync(NotificationId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> ListByRecipientAsync(Guid recipientUserId, int take, CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(Guid recipientUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> ListUnreadAsync(Guid recipientUserId, CancellationToken cancellationToken = default);
}
