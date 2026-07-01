namespace Marketplace.Modules.Sellers.Infrastructure.Storage;

/// <summary>
/// Configuration du stockage Cloudflare R2 (compatible S3) pour les pièces KYB.
/// Secrets hors du dépôt (deploy/.env + appsettings.Development.json). Tant que
/// les identifiants ne sont pas renseignés, un stub hors-ligne est utilisé.
/// Section de config : « Storage:CloudflareR2 ».
/// </summary>
public sealed class CloudflareR2Options
{
    /// <summary>Identifiant de compte Cloudflare (préfixe de l'endpoint R2).</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Access Key ID du jeton R2 (S3).</summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>Secret Access Key du jeton R2 (S3).</summary>
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Nom du bucket R2 recevant les pièces KYB.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Base URL publique pour lire les objets (domaine r2.dev du bucket, ou domaine
    /// personnalisé). Ex. « https://pub-xxxx.r2.dev ». L'URL retournée = base/clé.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Région S3 : R2 utilise « auto ».</summary>
    public string Region { get; set; } = "auto";

    /// <summary>Endpoint S3 de R2 déduit du compte.</summary>
    public string Endpoint => $"https://{AccountId}.r2.cloudflarestorage.com";

    /// <summary>L'adaptateur réel n'est actif que si les identifiants sont renseignés.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey)
        && !string.IsNullOrWhiteSpace(BucketName);
}
