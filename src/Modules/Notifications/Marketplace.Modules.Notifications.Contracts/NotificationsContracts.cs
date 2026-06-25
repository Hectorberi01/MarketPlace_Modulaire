namespace Marketplace.Modules.Notifications.Contracts;

/// <summary>Vue publique d'une notification.</summary>
public sealed record NotificationSummary(
    Guid Id,
    Guid RecipientUserId,
    string Channel,
    string Subject,
    string Body,
    string RelatedEntityType,
    Guid? RelatedEntityId,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);
