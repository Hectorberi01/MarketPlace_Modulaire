namespace Marketplace.Modules.Notifications.Domain.Preferences;

/// <summary>
/// Préférences de notification d'un utilisateur : la liste des CATÉGORIES dont il a
/// coupé les push (ex. « reviews »). Absence de ligne = tout est activé (le défaut
/// n'est jamais silencieux). Ne coupe QUE le push : la notification in-app reste
/// enregistrée dans la boîte de réception.
/// </summary>
public sealed class NotificationPreference
{
    public Guid UserId { get; private set; }

    /// <summary>Catégories dont le push est coupé (clés en minuscules). Mappé en text[].</summary>
    public List<string> MutedCategories { get; private set; } = new();

    public DateTime UpdatedAtUtc { get; private set; }

    private NotificationPreference() { } // EF

    public static NotificationPreference Create(Guid userId) => new()
    {
        UserId = userId,
        MutedCategories = new List<string>(),
        UpdatedAtUtc = DateTime.UtcNow,
    };

    public bool IsMuted(string category)
        => MutedCategories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));

    /// <summary>Remplace la liste des catégories coupées (normalisées, dédupliquées).</summary>
    public void Replace(IEnumerable<string> mutedCategories)
    {
        MutedCategories = mutedCategories
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
