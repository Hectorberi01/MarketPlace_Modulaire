namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Configuration Cloudinary pour le TRAITEMENT des images produit (détourage IA +
/// fond blanc). Cloudinary ne sert qu'au traitement : l'image finale est ensuite
/// rapatriée dans Cloudflare R2 par le flux de création, et l'asset Cloudinary est
/// détruit. Secrets hors du dépôt. Section de config : « Media:Cloudinary ».
/// Tant que les identifiants ne sont pas renseignés, un adaptateur no-op renvoie
/// l'image d'origine inchangée.
/// </summary>
public sealed class CloudinaryOptions
{
    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>Délai maximal d'attente (s) du rendu asynchrone avant abandon.</summary>
    public int MaxWaitSeconds { get; set; } = 25;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudName)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ApiSecret);
}
