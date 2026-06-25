namespace Marketplace.Modules.Notifications.Domain.Notifications;

/// <summary>Identité forte d'une notification.</summary>
public readonly record struct NotificationId(Guid Value)
{
    public static NotificationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Canal de distribution (in-app par défaut sur le socle).</summary>
public enum NotificationChannel
{
    InApp = 0,
    Email = 1,
    Sms = 2,
    Push = 3
}

/// <summary>Statut d'une notification.</summary>
public enum NotificationStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Read = 3
}
