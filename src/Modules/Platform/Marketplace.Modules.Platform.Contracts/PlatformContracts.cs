namespace Marketplace.Modules.Platform.Contracts;

/// <summary>
/// Canal d'un fournisseur, vu des autres modules.
///
/// Cette énumération DOUBLE celle du domaine, et ce n'est pas un oubli : aucun projet
/// Contracts du dépôt ne référence son propre Domain, et l'inverse non plus. Le
/// mapping se fait dans l'infrastructure du module, comme ShipmentMapper le fait pour
/// les modes de livraison. Le prix est cinq valeurs recopiées ; le gain est que les
/// modules consommateurs ne tirent pas le domaine de Platform.
/// </summary>
public enum PlatformChannel
{
    Payment = 0,
    Email = 1,
    Sms = 2,
    WhatsApp = 3,
    Push = 4
}

/// <summary>Mode d'exécution d'un fournisseur.</summary>
public enum PlatformProviderMode
{
    Sandbox = 0,
    Live = 1
}

/// <summary>
/// Configuration d'un fournisseur actif, prête à l'emploi.
/// </summary>
/// <param name="Secrets">
/// Valeurs EN CLAIR. Le déchiffrement a eu lieu dans le module Platform, qui seul
/// détient la clé maître. Ce dictionnaire ne doit JAMAIS être journalisé, sérialisé
/// dans une réponse HTTP, ni placé dans un message d'erreur.
/// </param>
public sealed record ProviderConfiguration(
    string Code,
    string DisplayName,
    PlatformChannel Channel,
    PlatformProviderMode Mode,
    int Priority,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> Secrets)
{
    /// <summary>Réglage non secret, ou la valeur de repli donnée.</summary>
    public string Setting(string key, string fallback = "")
        => Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    /// <summary>Secret en clair, ou chaîne vide s'il est absent.</summary>
    public string Secret(string key)
        => Secrets.TryGetValue(key, out var value) ? value : string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────────
// DTO de la surface d'administration. Ils ne portent JAMAIS une valeur de secret —
// seulement ses quatre premiers caractères, de quoi reconnaître quelle clé est en
// place sans permettre de s'en servir.
// ─────────────────────────────────────────────────────────────────────────────────

/// <summary>Un fournisseur que le code sait piloter, et les clés qu'il exige.</summary>
public sealed record ProviderCatalogEntry(
    string Code,
    string DisplayName,
    string Channel,
    IReadOnlyList<string> RequiredSettings,
    IReadOnlyList<string> RequiredSecrets,
    IReadOnlyList<string> OptionalSettings,
    bool SupportsSandbox);

/// <summary>Un fournisseur enregistré, tel que l'écran d'administration le voit.</summary>
/// <param name="SecretPreviews">Clé → quatre premiers caractères, ou vide si le secret est absent.</param>
/// <param name="MissingKeys">Clés exigées encore absentes : tant qu'elle n'est pas vide, l'activation est refusée.</param>
public sealed record ProviderSummary(
    Guid Id,
    string Channel,
    string Code,
    string DisplayName,
    string Mode,
    bool IsEnabled,
    int Priority,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> SecretPreviews,
    IReadOnlyList<string> MissingKeys,
    DateTime UpdatedAtUtc,
    Guid UpdatedByUserId);

/// <summary>Une ligne du journal des changements.</summary>
public sealed record ProviderChangeLogEntry(
    Guid Id,
    Guid ProviderId,
    string Channel,
    string Code,
    string Action,
    string Detail,
    Guid ActorUserId,
    DateTime AtUtc);
