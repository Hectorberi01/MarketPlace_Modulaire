using Microsoft.AspNetCore.Http;

namespace Marketplace.Api.Extensions;

/// <summary>
/// Validation partagée des fichiers uploadés (multipart) : présence, taille max
/// et allowlist de type MIME. Sans ces contrôles, un upload sans limite = DoS
/// mémoire/stockage, et sans contrôle de type = stockage de contenu arbitraire
/// (ex. exécutable déguisé en pièce KYB).
///
/// NB : le content-type est déclaré par le client (donc falsifiable) ; ce contrôle
/// est une première barrière. Un durcissement ultérieur peut vérifier les octets
/// d'en-tête (magic bytes) et/ou re-encoder les images.
/// </summary>
public static class UploadValidation
{
    public const long MaxImageBytes = 5 * 1024 * 1024;     // 5 Mo
    public const long MaxDocumentBytes = 10 * 1024 * 1024; // 10 Mo

    public static readonly string[] ImageTypes = { "image/jpeg", "image/png", "image/webp" };
    public static readonly string[] DocumentTypes = { "image/jpeg", "image/png", "image/webp", "application/pdf" };

    /// <summary>
    /// Valide un fichier uploadé. Renvoie un <see cref="IResult"/> d'erreur (400)
    /// si invalide (à retourner tel quel par l'endpoint), sinon <c>null</c>.
    /// </summary>
    public static IResult? Validate(IFormFile? file, long maxBytes, IReadOnlyCollection<string> allowedContentTypes)
    {
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { error = "Fichier manquant ou vide." });
        }
        if (file.Length > maxBytes)
        {
            return Results.BadRequest(new { error = $"Fichier trop volumineux (max {maxBytes / (1024 * 1024)} Mo)." });
        }

        var contentType = file.ContentType?.Split(';')[0].Trim().ToLowerInvariant() ?? string.Empty;
        if (!allowedContentTypes.Contains(contentType))
        {
            return Results.BadRequest(new { error = $"Type de fichier non autorisé : {file.ContentType}." });
        }

        return null;
    }

    /// <summary>Valide une image (JPEG/PNG/WebP, ≤ 5 Mo).</summary>
    public static IResult? ValidateImage(IFormFile? file) => Validate(file, MaxImageBytes, ImageTypes);

    /// <summary>Valide un document (image ou PDF, ≤ 10 Mo) — pièces KYB, justificatifs, pièces jointes.</summary>
    public static IResult? ValidateDocument(IFormFile? file) => Validate(file, MaxDocumentBytes, DocumentTypes);
}
