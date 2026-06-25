using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Domain.Notifications;

namespace Marketplace.Modules.Notifications.Application.Notifications;

/// <summary>
/// Crée et « distribue » une notification in-app. Point d'entrée unique des
/// consumers d'events : centralise la création + l'envoi + la persistance.
/// Brancher un Email/SMS reviendrait à ajouter un canal ici, sans toucher aux
/// consumers.
/// </summary>
public sealed class NotificationDispatcher
{
    private readonly INotificationRepository _repository;
    private readonly INotificationsUnitOfWork _unitOfWork;

    public NotificationDispatcher(INotificationRepository repository, INotificationsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task NotifyAsync(
        Guid recipientUserId, string subject, string body, string relatedType, Guid? relatedId, CancellationToken ct)
    {
        var result = Notification.Create(recipientUserId, NotificationChannel.InApp, subject, body, relatedType, relatedId);
        if (result.IsFailure)
        {
            return;
        }

        var notification = result.Value;
        notification.MarkSent(); // canal in-app : distribué instantanément dans la boîte de réception
        await _repository.AddAsync(notification, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
