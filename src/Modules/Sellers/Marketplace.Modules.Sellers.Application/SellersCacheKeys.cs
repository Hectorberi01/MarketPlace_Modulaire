namespace Marketplace.Modules.Sellers.Application;

/// <summary>
/// Clés de cache du module Sellers.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CE MODULE A BESOIN D'UN CACHE, ALORS QU'IL N'EST PAS « CHAUD »
///
/// Il l'est, mais indirectement — et c'est ce qui rendait le problème invisible.
///
/// La fiche produit mobile agrège les offres du produit, puis résout le nom de
/// boutique de CHAQUE vendeur, un par un, dans une boucle (voir
/// MobileCatalogEndpoints.ProductDetailAsync). Chaque tour de boucle était une
/// requête SQL — et chacune chargeait le vendeur AVEC tous ses documents KYB, pour
/// n'en retenir que le nom de la boutique.
///
/// Un produit vendu par trois marchands, c'était donc trois requêtes lourdes, à
/// chaque affichage de fiche, pour trois chaînes de caractères qui ne changent
/// jamais.
///
/// C'est le N+1 le plus coûteux de l'application, et il ne se voyait dans aucun
/// endpoint : il se cachait dans une boucle.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public static class SellersCacheKeys
{
    /// <summary>Résumé d'un vendeur, par identifiant de vendeur.</summary>
    public static string Seller(Guid sellerId) => $"sellers:seller:{sellerId}";

    /// <summary>Résumé d'un vendeur, par identifiant d'utilisateur (connexion vendeur).</summary>
    public static string SellerByUser(Guid userId) => $"sellers:by-user:{userId}";

    /// <summary>
    /// 10 minutes.
    ///
    /// Un nom de boutique, un logo, un statut : ça ne bouge quasiment jamais, et
    /// toute écriture invalide la clé sur-le-champ. Ce TTL n'est qu'un filet.
    /// </summary>
    public static readonly TimeSpan SellerTtl = TimeSpan.FromMinutes(10);

    /// <summary>Mémorisation d'une absence — protège les endpoints anonymes.</summary>
    public static readonly TimeSpan MissTtl = TimeSpan.FromSeconds(30);
}
