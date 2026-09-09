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
        string subject, string body, string relatedEntityType, Guid? relatedEntityId,
        string? deduplicationKey)
        : base(id)
    {
        RecipientUserId = recipientUserId;
        Channel = channel;
        Subject = subject;
        Body = body;
        RelatedEntityType = relatedEntityType;
        RelatedEntityId = relatedEntityId;
        DeduplicationKey = deduplicationKey;
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

    /// <summary>
    /// Clé « une seule fois », ou null pour une notification sans contrainte d'unicité.
    ///
    /// ════════════════════════════════════════════════════════════════════════════
    /// RIEN N'EMPÊCHAIT DE NOTIFIER DIX FOIS LE MÊME FAIT, ET C'EST ARRIVÉ.
    ///
    /// La distribution d'événements est « au moins une fois » : l'outbox rejoue un
    /// message tant qu'un de ses consommateurs échoue, jusqu'à dix tentatives. Or
    /// Notifications est dispatché AVANT Settlement, Loyalty et Analytics
    /// (`ModuleRegistry`) : une panne chez l'un d'eux rejouait le message ENTIER, donc
    /// re-notifiait. L'acheteur recevait jusqu'à dix fois « Votre paiement a été reçu »,
    /// chaque vendeur dix fois « Nouvelle commande à préparer » — notification in-app,
    /// push ET e-mail à chaque tour.
    ///
    /// La clé porte le FAIT notifié, pas la notification : « cette commande confirmée,
    /// pour ce destinataire ». Un index UNIQUE partiel (posé seulement quand la clé
    /// n'est pas nulle) en fait une garantie de BASE et non une simple précaution
    /// applicative : deux tentatives concurrentes ne peuvent pas passer toutes les
    /// deux, quel que soit l'entrelacement.
    ///
    /// La colonne est NULLABLE parce que la majorité des notifications sont légitimement
    /// répétables — « nouveau message », « colis en transit » — et ne doivent surtout
    /// pas tomber sous une contrainte d'unicité.
    /// ════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string? DeduplicationKey { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    /// <param name="deduplicationKey">
    /// Clé d'unicité optionnelle — voir <see cref="DeduplicationKey"/>. À renseigner pour
    /// un fait qui ne doit produire QU'UNE notification, même si son événement est rejoué.
    /// </param>
    public static Result<Notification> Create(
        Guid recipientUserId, NotificationChannel channel, string subject, string body,
        string relatedEntityType, Guid? relatedEntityId, string? deduplicationKey = null)
    {
        if (recipientUserId == Guid.Empty)
        {
            return Error.Validation("notifications.recipient_required", "Le destinataire est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return Error.Validation("notifications.subject_required", "Le sujet est obligatoire.");
        }

        // Une clé vide vaut « pas de clé » : sans cette normalisation, deux appels
        // négligents avec "" se collisionneraient sur l'index unique et le second
        // échouerait en boucle jusqu'à la lettre morte.
        var normalizedKey = string.IsNullOrWhiteSpace(deduplicationKey) ? null : deduplicationKey.Trim();

        return new Notification(
            NotificationId.New(), recipientUserId, channel, subject.Trim(),
            (body ?? string.Empty).Trim(), relatedEntityType ?? string.Empty, relatedEntityId,
            normalizedKey);
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
