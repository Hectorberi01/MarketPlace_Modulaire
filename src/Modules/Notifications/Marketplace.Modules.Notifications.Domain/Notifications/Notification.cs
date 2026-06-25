using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Notifications.Domain.Notifications;

/// <summary>
/// Notification adressée à un utilisateur, déclenchée par un fait métier d'un
/// autre module (commande, expédition…). Sur le socle, le canal in-app est
/// « envoyé » instantanément ; les canaux Email/SMS brancheraient un prestataire.
/// </summary>
public sealed class Notification : AggregateRoot<NotificationId>
{
    private Notification()
    {
    }

    private Notification(
        NotificationId id, Guid recipientUserId, NotificationChannel channel,
        string subject, string body, string relatedEntityType, Guid? relatedEntityId)
        : base(id)
    {
        RecipientUserId = recipientUserId;
        Channel = channel;
        Subject = subject;
        Body = body;
        RelatedEntityType = relatedEntityType;
        RelatedEntityId = relatedEntityId;
        Status = NotificationStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid RecipientUserId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string Subject { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public string RelatedEntityType { get; private set; } = default!;
    public Guid? RelatedEntityId { get; private set; }
    public NotificationStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    public static Result<Notification> Create(
        Guid recipientUserId, NotificationChannel channel, string subject, string body,
        string relatedEntityType, Guid? relatedEntityId)
    {
        if (recipientUserId == Guid.Empty)
        {
            return Error.Validation("notifications.recipient_required", "Le destinataire est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return Error.Validation("notifications.subject_required", "Le sujet est obligatoire.");
        }

        return new Notification(
            NotificationId.New(), recipientUserId, channel, subject.Trim(),
            (body ?? string.Empty).Trim(), relatedEntityType ?? string.Empty, relatedEntityId);
    }

    /// <summary>Marque la notification comme distribuée.</summary>
    public void MarkSent()
    {
        Status = NotificationStatus.Sent;
        SentAtUtc = DateTime.UtcNow;
    }

    /// <summary>Marque l'échec de distribution.</summary>
    public void MarkFailed() => Status = NotificationStatus.Failed;

    /// <summary>Marque la notification comme lue par le destinataire.</summary>
    public Result MarkRead()
    {
        if (Status == NotificationStatus.Read)
        {
            return Result.Success();
        }

        Status = NotificationStatus.Read;
        ReadAtUtc = DateTime.UtcNow;
        return Result.Success();
    }
}
