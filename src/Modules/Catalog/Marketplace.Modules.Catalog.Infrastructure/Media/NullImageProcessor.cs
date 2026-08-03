using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Abstractions;

namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Adaptateur « no-op » utilisé quand Cloudinary n'est pas configuré : renvoie
/// l'image d'origine inchangée (le produit se crée alors sans détourage).
/// </summary>
public sealed class NullImageProcessor : IImageProcessor, IImageProcessingAvailability
{
    /// <summary>Rien n'est traité ici : le détourage n'est PAS disponible.</summary>
    public bool IsAvailable => false;

    public Task<Result<ProcessedImage>> RemoveBackgroundWhiteAsync(
        string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success(new ProcessedImage(
            content, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType)));
}
