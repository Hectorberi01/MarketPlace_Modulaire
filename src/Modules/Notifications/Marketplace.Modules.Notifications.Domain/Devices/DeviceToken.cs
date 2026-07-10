namespace Marketplace.Modules.Notifications.Domain.Devices;

/// <summary>
/// Jeton d'appareil (FCM) associé à un utilisateur, pour l'envoi de notifications
/// push. Un même jeton est unique (une installation d'app) et peut être réassigné
/// si l'utilisateur change sur l'appareil.
/// </summary>
public sealed class DeviceToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Token { get; private set; } = string.Empty;

    /// <summary>Plateforme : « android », « ios » ou « web ».</summary>
    public string Platform { get; private set; } = "unknown";

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastSeenAtUtc { get; private set; }

    private DeviceToken() { } // EF

    public static DeviceToken Create(Guid userId, string token, string platform)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token.Trim(),
            Platform = Normalize(platform),
            CreatedAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow,
        };

    /// <summary>Réassocie le jeton à l'utilisateur courant (ex. changement de compte sur l'appareil).</summary>
    public void Reassign(Guid userId, string platform)
    {
        UserId = userId;
        Platform = Normalize(platform);
        LastSeenAtUtc = DateTime.UtcNow;
    }

    private static string Normalize(string platform)
        => string.IsNullOrWhiteSpace(platform) ? "unknown" : platform.Trim().ToLowerInvariant();
}
