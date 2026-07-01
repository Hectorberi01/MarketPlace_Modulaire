using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Catalog.Application.Abstractions;

/// <summary>Fichier à téléverser vers le service média (contenu binaire en mémoire).</summary>
public sealed record MediaUpload(string FileName, string ContentType, byte[] Content, string? AltText = null);

/// <summary>Média stocké côté service externe : URL publique + identifiant distant.</summary>
public sealed record StoredMedia(string Url, string ExternalId, string ContentType);

/// <summary>
/// Port d'intégration au service média externe (https://hbamediacore.fr). Le module
/// Catalog ne stocke jamais les fichiers lui-même : il délègue le stockage au
/// service, qui renvoie l'URL publique à associer au produit. Implémenté en
/// Infrastructure (adaptateur HTTP réel ou stub hors-ligne).
/// </summary>
public interface IMediaStorage
{
    /// <summary>Téléverse un fichier et renvoie son URL publique.</summary>
    Task<Result<StoredMedia>> UploadAsync(MediaUpload file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Téléverse plusieurs fichiers en une seule requête (endpoint « batch ») et
    /// renvoie les médias stockés dans le même ordre. Une liste vide renvoie un
    /// résultat vide sans appel réseau.
    /// </summary>
    Task<Result<IReadOnlyList<StoredMedia>>> UploadManyAsync(IReadOnlyList<MediaUpload> files, CancellationToken cancellationToken = default);

    /// <summary>
    /// Supprime un fichier côté service média à partir de son identifiant distant.
    /// Un externalId vide est traité comme un succès (rien à supprimer à distance).
    /// </summary>
    Task<Result> DeleteAsync(string externalId, CancellationToken cancellationToken = default);
}
