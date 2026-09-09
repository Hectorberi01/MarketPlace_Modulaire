namespace Marketplace.Modules.Catalog.Contracts;

/// <summary>
/// API in-process publique du module Catalog. C'est le SEUL moyen pour un autre
/// module (Offers, Cart…) de lire du catalogue de façon synchrone — jamais en
/// touchant la base ou les entités internes. Le jour de l'extraction, cette
/// interface devient un client HTTP/gRPC sans changer les appelants.
/// </summary>
public interface ICatalogModuleApi
{
    /// <summary>
    /// Lecture INTERNE d'un produit, TOUS STATUTS CONFONDUS.
    ///
    /// Ne convient PAS à une surface anonyme : voir
    /// <see cref="GetPubliclyVisibleProductAsync"/>. Cette méthode existe pour les
    /// appelants qui doivent voir un produit quel que soit son statut — le vendeur
    /// sur sa propre fiche, l'administration, l'indexeur de recherche, et surtout
    /// les lignes de panier et de commande, qui décrivent un article ACHETÉ et
    /// doivent continuer de l'afficher même s'il a été archivé depuis.
    /// </summary>
    Task<ProductSummary?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LECTURE PUBLIQUE : NUL SI LE PRODUIT N'EST PAS EN VENTE.
    ///
    /// La validation d'un produit par l'administration ne protégeait que
    /// l'INDEXATION. `GetProductAsync` ne filtrait aucun statut, et aucun BFF ne
    /// testait `Status` : un brouillon jamais validé, ou un produit REJETÉ par un
    /// modérateur — le rejet écrit « Archived » — restait intégralement servi à tout
    /// anonyme connaissant l'identifiant. Nom, description, images, variantes, et
    /// jusqu'au champ `Status` lui-même, qui livrait au client la preuve que la fiche
    /// n'aurait pas dû lui être servie.
    ///
    /// Le pire chemin était `/p/{id}` : une page HTML publique avec balises Open
    /// Graph, donc partageable et récupérable par un robot d'indexation.
    ///
    /// Deux méthodes plutôt qu'un paramètre : un booléen s'oublie à l'appel, et
    /// l'oubli est silencieux. Un nom qui dit ce qu'il garantit ne s'oublie pas.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<ProductSummary?> GetPubliclyVisibleProductAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Produits « mis en avant » : actifs et porteurs du tag <c>featured</c>.
    /// Sert la section éditoriale de l'accueil. Le tag est le drapeau (aucune
    /// colonne dédiée) ; on n'en garde qu'un nombre limité, les plus récents.
    /// </summary>
    Task<IReadOnlyList<ProductSummary>> ListFeaturedAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// PARCOURS COMPLET DU CATALOGUE, PAR PAGES, POUR RECONSTRUIRE UN READ MODEL.
    ///
    /// Aucune reconstruction d'index n'existait : ni commande, ni route, ni service
    /// de fond. Toute divergence était donc DÉFINITIVE — un message parti en lettre
    /// morte, un `OUTBOX_ENABLED=false` posé par erreur, ou simplement les produits
    /// renommés avant que l'événement de mise à jour n'existe. Et le rejeu unitaire
    /// d'une lettre morte ne sert à rien quand l'événement n'a jamais été produit.
    ///
    /// PAGINATION PAR CURSEUR, PAS PAR OFFSET. Une reconstruction lit tout le
    /// catalogue : un `Skip(n)` coûterait de plus en plus cher à chaque page, et
    /// surtout il DÉCALERAIT les pages suivantes si un produit était créé ou
    /// supprimé pendant le parcours — donc sauterait des lignes, en silence. Le
    /// curseur sur l'identifiant est stable quoi qu'il arrive entre deux pages.
    ///
    /// Renvoie une projection MINIMALE : reconstruire l'index n'a pas besoin des
    /// variantes ni de toutes les images, et les charger multiplierait le coût du
    /// balayage par le nombre de lignes filles.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="afterId">Dernier identifiant de la page précédente ; null pour la première.</param>
    /// <param name="take">Taille de page demandée (bornée par l'implémentation).</param>
    Task<IReadOnlyList<ProductIndexEntry>> ListForIndexingAsync(
        Guid? afterId, int take, CancellationToken cancellationToken = default);

    Task<BrandSummary?> GetBrandAsync(Guid brandId, CancellationToken cancellationToken = default);

    Task<CategorySummary?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
