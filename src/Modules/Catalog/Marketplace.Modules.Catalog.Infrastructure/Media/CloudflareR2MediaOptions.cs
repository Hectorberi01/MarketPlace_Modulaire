namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Configuration Cloudflare R2 (compatible S3) pour les IMAGES PRODUIT.
/// Contrairement aux pièces KYB (privées), les images produit sont PUBLIQUES
/// (lisibles par les acheteurs) : le bucket doit exposer un domaine public
/// (r2.dev ou domaine personnalisé) renseigné dans <see cref="PublicBaseUrl"/>.
/// Secrets hors du dépôt. Section de config : « Storage:CloudflareR2Media ».
/// Tant que les identifiants ne sont pas renseignés, le stub/HBA reste utilisé.
/// </summary>
public sealed class CloudflareR2MediaOptions
{
    public string AccountId { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;

    /// <summary>Base URL publique de lecture des images (obligatoire pour l'affichage acheteur).</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Préfixe de clé (dossier) où ranger les images dans le bucket. Vide = racine.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    public string Region { get; set; } = "auto";

    public string Endpoint => $"https://{AccountId}.r2.cloudflarestorage.com";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey)
        && !string.IsNullOrWhiteSpace(BucketName)
        && !string.IsNullOrWhiteSpace(PublicBaseUrl);
}
