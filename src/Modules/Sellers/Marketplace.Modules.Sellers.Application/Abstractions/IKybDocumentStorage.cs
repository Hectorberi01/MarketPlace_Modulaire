using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Sellers.Application.Abstractions;

/// <summary>Pièce KYB à téléverser (contenu binaire en mémoire).</summary>
public sealed record KybFileUpload(string FileName, string ContentType, byte[] Content);

/// <summary>Pièce KYB stockée : URL d'accès + clé de l'objet côté stockage.</summary>
public sealed record StoredKybFile(string Url, string ObjectKey);

/// <summary>
/// Port de stockage des documents KYB des vendeurs. Contrairement aux images
/// produit (service média public), les pièces KYB sont sensibles : elles sont
/// stockées sur Cloudflare R2 (compatible S3). Implémenté en Infrastructure
/// (adaptateur R2 réel si configuré, sinon stub hors-ligne).
/// </summary>
public interface IKybDocumentStorage
{
    Task<Result<StoredKybFile>> UploadAsync(KybFileUpload file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Génère une URL de lecture temporaire (présignée) pour consulter un document
    /// KYB privé, à partir de sa clé d'objet. Réservé à la vérification admin.
    /// </summary>
    Result<string> CreatePresignedGetUrl(string objectKey, int expiresSeconds = 300);
}
