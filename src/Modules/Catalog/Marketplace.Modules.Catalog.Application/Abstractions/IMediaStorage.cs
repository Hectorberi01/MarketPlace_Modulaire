using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Catalog.Application.Abstractions;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// À QUOI SERT LE FICHIER — ET DONC OÙ IL EST RANGÉ.
///
/// Tous les téléversements de la place passaient par le même appel, et atterrissaient
/// donc dans le même bucket, sous la même racine de clé « products/ » : photo produit,
/// logo de boutique, visuel de rayon, et PIÈCE JOINTE D'UNE CONVERSATION entre un
/// acheteur et un vendeur. Le stockage n'avait aucun moyen de les distinguer, parce
/// que l'appelant ne le lui disait pas.
///
/// Cette énumération est cette information manquante. Elle n'est PAS une commodité de
/// rangement : c'est elle qui décide du bucket, donc du domaine public qui sert le
/// fichier, donc de qui peut le lire. Un nouvel usage s'ajoute ici, et nulle part
/// ailleurs — la table de correspondance vit dans les options du stockage.
///
/// AUCUNE VALEUR PAR DÉFAUT, VOLONTAIREMENT. `Usage` est un paramètre obligatoire de
/// <see cref="MediaUpload"/>. Un défaut aurait rangé silencieusement dans le bucket
/// des produits tout appelant qui aurait oublié de se déclarer — c'est-à-dire
/// exactement le défaut qu'on corrige.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum MediaUsage
{
    /// <summary>Photo de fiche produit. Public, indexable, servi à tout acheteur.</summary>
    ProductImage,

    /// <summary>Logo d'une boutique vendeur. Public.</summary>
    ShopLogo,

    /// <summary>Visuel de couverture (arrière-plan) d'une boutique vendeur. Public.</summary>
    ShopBanner,

    /// <summary>Visuel de rayon, de famille ou de catégorie. Public.</summary>
    CategoryImage,

    /// <summary>Bannière éditoriale de l'accueil ou d'une page de contenu. Public.</summary>
    ContentBanner,

    /// <summary>Photo de profil d'un compte. Public.</summary>
    Avatar,

    /// <summary>
    /// Pièce jointe déposée dans une conversation ou un litige.
    ///
    /// CE CONTENU N'EST PAS DESTINÉ AU PUBLIC. Le séparer dans son propre bucket est
    /// la première moitié du travail : cela permet de lui retirer son domaine public
    /// et de basculer sa lecture en URL présignée SANS retoucher une ligne
    /// d'appelant. Tant que ce bucket expose un domaine public, l'URL reste lisible
    /// par quiconque la possède — voir « Structure_Buckets_R2.md ».
    /// </summary>
    ChatAttachment
}

/// <summary>
/// Fichier à téléverser vers le service média (contenu binaire en mémoire).
/// <paramref name="Usage"/> décide du bucket de destination : voir <see cref="MediaUsage"/>.
/// </summary>
public sealed record MediaUpload(
    string FileName,
    string ContentType,
    byte[] Content,
    MediaUsage Usage,
    string? AltText = null);

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
