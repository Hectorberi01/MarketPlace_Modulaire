using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Domain.Devices;
using Marketplace.Modules.Notifications.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace Marketplace.Modules.Notifications.Application.Notifications;

/// <summary>
/// Crée et « distribue » une notification. Point d'entrée unique des consumers
/// d'events : centralise la création + la persistance in-app + l'envoi PUSH.
/// Le push est best-effort : une panne FCM ne doit jamais empêcher la notif in-app.
/// </summary>
public sealed class NotificationDispatcher
{
    private readonly INotificationRepository _repository;
    private readonly INotificationsUnitOfWork _unitOfWork;
    private readonly IDeviceTokenRepository _deviceTokens;
    private readonly IPushSender _pushSender;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        INotificationRepository repository,
        INotificationsUnitOfWork unitOfWork,
        IDeviceTokenRepository deviceTokens,
        IPushSender pushSender,
        ILogger<NotificationDispatcher> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _deviceTokens = deviceTokens;
        _pushSender = pushSender;
        _logger = logger;
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

        // Envoi push vers les appareils du destinataire (best-effort).
        await SendPushAsync(recipientUserId, subject, body, relatedType, relatedId, ct);
    }

    /// <summary>
    /// Envoie uniquement le push (in-app déjà persistée par ailleurs) vers les
    /// appareils du destinataire. Best-effort : une panne FCM n'interrompt rien.
    /// Exposé pour les chemins qui créent la notif eux-mêmes (ex. envoi admin).
    /// </summary>
    public async Task SendPushAsync(
        Guid userId, string title, string body, string relatedType, Guid? relatedId, CancellationToken ct)
    {
        try
        {
            var devices = await _deviceTokens.ListByUserAsync(userId, ct);

            // DIAGNOSTIC : quel adaptateur d'envoi est actif (FcmPushSender vs
            // NullPushSender) et combien d'appareils sont ciblés pour cet utilisateur.
            _logger.LogInformation(
                "Push: user={UserId} sender={Sender} devices={DeviceCount}",
                userId, _pushSender.GetType().Name, devices.Count);

            if (devices.Count == 0)
            {
                _logger.LogWarning(
                    "Push: aucun appareil enregistré pour user={UserId} — le jeton FCM n'a pas été reçu/enregistré côté app.",
                    userId);
                return;
            }

            var tokens = devices.Select(d => d.Token).ToList();
            var data = new Dictionary<string, string> { ["type"] = relatedType ?? string.Empty };
            if (relatedId is { } id)
            {
                data["entityId"] = id.ToString();
            }

            var res = await _pushSender.SendAsync(tokens, new PushMessage(title, body, data), ct);

            _logger.LogInformation(
                "Push: envoi terminé pour user={UserId} — jetons={TokenCount}, invalides={InvalidCount}.",
                userId, tokens.Count, res.InvalidTokens.Count);

            // Purge des jetons devenus invalides (app désinstallée, jeton périmé).
            if (res.InvalidTokens.Count > 0)
            {
                await _deviceTokens.RemoveByTokensAsync(res.InvalidTokens, ct);
                await _unitOfWork.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            // Le push ne doit jamais casser le flux de notification in-app, mais on
            // trace l'échec (sinon diagnostic impossible).
            _logger.LogError(ex, "Push: échec d'envoi pour user={UserId}.", userId);
        }
    }
}
