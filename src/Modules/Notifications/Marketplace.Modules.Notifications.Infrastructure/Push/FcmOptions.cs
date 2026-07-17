namespace Marketplace.Modules.Notifications.Infrastructure.Push;

/// <summary>
/// Configuration FCM (Firebase Cloud Messaging). On fournit le JSON du compte de
/// service Google, soit en ligne (<see cref="ServiceAccountJson"/>), soit via un
/// fichier monté (<see cref="ServiceAccountPath"/>). Secrets hors dépôt.
/// Section de config : « Notifications:Fcm ».
/// </summary>
public sealed class FcmOptions
{
    /// <summary>Contenu JSON du compte de service (clé privée). Prioritaire s'il est renseigné.</summary>
    public string ServiceAccountJson { get; set; } = string.Empty;

    /// <summary>Chemin vers le fichier JSON du compte de service (alternative à l'inline).</summary>
    public string ServiceAccountPath { get; set; } = string.Empty;

    /// <summary>Résout le JSON du compte de service (inline ou fichier), ou null si absent.</summary>
    public string? ResolveJson()
    {
        if (!string.IsNullOrWhiteSpace(ServiceAccountJson))
        {
            return ServiceAccountJson;
        }
        if (!string.IsNullOrWhiteSpace(ServiceAccountPath) && File.Exists(ServiceAccountPath))
        {
            return File.ReadAllText(ServiceAccountPath);
        }
        return null;
    }

    public bool IsConfigured => ResolveJson() is not null;
}
