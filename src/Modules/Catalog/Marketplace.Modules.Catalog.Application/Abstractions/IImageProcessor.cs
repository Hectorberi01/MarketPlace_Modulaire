using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Catalog.Application.Abstractions;

/// <summary>Image traitée : contenu binaire prêt à stocker + type MIME.</summary>
public sealed record ProcessedImage(byte[] Content, string ContentType);

/// <summary>
/// Port de traitement d'image (détourage IA + fond blanc). Implémenté en
/// Infrastructure via Cloudinary. Quand le service n'est pas configuré, un
/// adaptateur « no-op » renvoie l'image d'origine inchangée pour ne pas bloquer
/// la création de produit.
/// </summary>
public interface IImageProcessor
{
    /// <summary>
    /// Retire l'arrière-plan de l'image (IA) puis l'aplatit sur fond blanc (JPEG).
    /// L'image finale est destinée à être stockée dans R2 par le flux de création.
    /// </summary>
    Task<Result<ProcessedImage>> RemoveBackgroundWhiteAsync(
        string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default);
}
