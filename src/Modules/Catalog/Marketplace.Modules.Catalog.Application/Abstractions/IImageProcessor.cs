using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Catalog.Application.Abstractions;

/// <summary>Image traitée : contenu binaire prêt à stocker + type MIME.</summary>
public sealed record ProcessedImage(byte[] Content, string ContentType);

/// <summary>
/// Marqueur de disponibilité RÉELLE du traitement d'image.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// POURQUOI CE DRAPEAU EXISTE
///
/// Quand Cloudinary n'est pas configuré, `NullImageProcessor` renvoie l'image
/// D'ORIGINE et un `Result.Success`. Du point de vue de l'appelant, le traitement a
/// « réussi » — et les interfaces affichaient fièrement « Détourée » sur une photo
/// strictement inchangée. Le vendeur, lui, voyait un bouton qui ne fait rien et n'avait
/// aucun moyen de comprendre pourquoi.
///
/// Le repli silencieux garde tout son sens pour la CRÉATION de produit (mieux vaut une
/// photo brute qu'un blocage). Il n'en a aucun pour un bouton dédié au détourage : là,
/// il faut pouvoir dire que le service n'est pas disponible.
/// ─────────────────────────────────────────────────────────────────────────────────
/// </summary>
public interface IImageProcessingAvailability
{
    /// <summary>Faux quand le traitement est un no-op (service non configuré).</summary>
    bool IsAvailable { get; }
}

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
