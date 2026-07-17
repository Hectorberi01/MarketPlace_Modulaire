namespace Marketplace.Modules.Offers.Application;

/// <summary>
/// Clés de cache du module Offers.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// ⚠️ CE MODULE PORTE LES PRIX. IL NE SE CACHE PAS COMME LES AUTRES.
///
/// Un nom de produit périmé de cinq minutes n'ennuie personne. Un PRIX périmé de
/// cinq minutes est une promesse commerciale que l'on ne tient pas : l'acheteur voit
/// 12 000 F, arrive au paiement, et découvre 14 000 F. Il ne conclut pas que le
/// cache a mal fait son travail — il conclut qu'on l'a trompé.
///
/// D'où un TTL de 60 secondes, contre 5 à 30 minutes pour le catalogue. C'est
/// délibérément court : le cache absorbe ici les rafales (le pic de consultation
/// d'un même produit), il ne prétend pas mémoriser durablement.
///
/// L'invalidation à l'écriture reste la vraie garantie ; le TTL n'est que le filet
/// tendu dessous.
///
/// ⚠️ NE PAS ALLONGER CE TTL pour « gagner un peu de base de données ». Le gain
/// serait marginal — les offres changent rarement, donc le cache est déjà chaud —
/// et le prix à payer serait un affichage de prix faux. Si la base souffre, c'est
/// ailleurs qu'il faut chercher.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public static class OffersCacheKeys
{
    /// <summary>Offre unitaire.</summary>
    public static string Offer(Guid offerId) => $"offers:offer:{offerId}";

    /// <summary>
    /// Offres actives d'un produit. Lue à CHAQUE affichage de fiche produit — c'est
    /// la clé chaude du module.
    /// </summary>
    public static string OffersByProduct(Guid productId) => $"offers:by-product:{productId}";

    /// <summary>60 secondes. Voir l'avertissement ci-dessus avant de toucher à cette valeur.</summary>
    public static readonly TimeSpan OfferTtl = TimeSpan.FromSeconds(60);

    /// <summary>Mémorisation d'une absence, sur un chemin anonyme.</summary>
    public static readonly TimeSpan MissTtl = TimeSpan.FromSeconds(30);
}
