using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Application.Emails;
using Marketplace.Modules.Notifications.Domain.Devices;
using Marketplace.Modules.Notifications.Domain.Notifications;
using Marketplace.Modules.Notifications.Domain.Preferences;
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
    private readonly INotificationPreferenceRepository _preferences;
    private readonly IPushSender _pushSender;
    private readonly IEmailSender _emailSender;
    private readonly IIdentityModuleApi _identity;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        INotificationRepository repository,
        INotificationsUnitOfWork unitOfWork,
        IDeviceTokenRepository deviceTokens,
        INotificationPreferenceRepository preferences,
        IPushSender pushSender,
        IEmailSender emailSender,
        IIdentityModuleApi identity,
        ILogger<NotificationDispatcher> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _deviceTokens = deviceTokens;
        _preferences = preferences;
        _pushSender = pushSender;
        _emailSender = emailSender;
        _identity = identity;
        _logger = logger;
    }

    /// <param name="alsoEmail">
    /// Double la notification par un e-mail. À réserver aux étapes que l'utilisateur doit
    /// connaître même sans l'application : commande confirmée, colis expédié/livré,
    /// remboursement, litige tranché. Le push seul ne suffit pas — application
    /// désinstallée, notifications refusées, téléphone changé.
    /// </param>
    public async Task NotifyAsync(
        Guid recipientUserId, string subject, string body, string relatedType, Guid? relatedId, CancellationToken ct,
        bool alsoEmail = false)
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

        if (alsoEmail)
        {
            await SendEmailAsync(recipientUserId, subject, body, ct);
        }
    }

    /// <summary>
    /// Double la notification par e-mail (adresse résolue via Identity).
    ///
    /// ⚠️ BEST-EFFORT DÉLIBÉRÉ — contrairement aux e-mails de compte, qui doivent lever.
    ///
    /// À ce stade, la notification in-app est DÉJÀ persistée et le push DÉJÀ parti. Si on
    /// laissait l'échec d'envoi remonter, l'outbox rejouerait le message entier : le
    /// destinataire recevrait une seconde notification in-app et un second push pour le
    /// même événement. On préfère un e-mail manquant — tracé en erreur — à un doublon de
    /// notifications à chaque hoquet du fournisseur.
    /// </summary>
    private async Task SendEmailAsync(Guid userId, string subject, string body, CancellationToken ct)
    {
        try
        {
            var user = await _identity.GetUserAsync(userId, ct);
            if (user is null || string.IsNullOrWhiteSpace(user.Email))
            {
                _logger.LogWarning("E-mail: user={UserId} introuvable ou sans adresse — e-mail non envoyé.", userId);
                return;
            }

            await _emailSender.SendAsync(
                AccountEmailTemplates.Transactional(user.Email, user.FirstName, subject, body), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "E-mail: échec d'envoi pour user={UserId} (sujet « {Subject} »).", userId, subject);
        }
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
            // Préférence vendeur : si la catégorie de cette notification a été coupée,
            // on n'envoie PAS le push (la notif in-app reste enregistrée par ailleurs).
            var category = NotificationCategories.FromRelatedType(relatedType);
            if (category is not null)
            {
                var pref = await _preferences.GetByUserAsync(userId, ct);
                if (pref is not null && pref.IsMuted(category))
                {
                    _logger.LogInformation(
                        "Push: user={UserId} catégorie « {Category} » coupée — push ignoré.", userId, category);
                    return;
                }
            }

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
