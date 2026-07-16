namespace Marketplace.Modules.Reviews.Application;

/// <summary>
/// Clés de cache du module Reviews.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LA NOTE AGRÉGÉE EST LA REQUÊTE LA PLUS COÛTEUSE DE LA FICHE PRODUIT.
///
/// Les autres lectures cherchent une ligne par sa clé primaire — Postgres les sert
/// en une fraction de milliseconde. La note, elle, est une AGRÉGATION : moyenne et
/// comptage sur tous les avis publiés du produit. Son coût croît avec le succès du
/// produit — c'est-à-dire que les produits les plus consultés sont exactement ceux
/// dont la note est la plus chère à recalculer.
///
/// Et elle était recalculée à CHAQUE affichage de fiche, pour renvoyer deux nombres
/// qui ne bougent que lorsqu'un avis est publié.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public static class ReviewsCacheKeys
{
    /// <summary>Note agrégée d'un produit (moyenne + nombre d'avis).</summary>
    public static string Rating(Guid productId) => $"reviews:rating:{productId}";

    /// <summary>Avis publiés d'un produit (onglet « Avis » de la fiche).</summary>
    public static string ByProduct(Guid productId) => $"reviews:by-product:{productId}";

    /// <summary>
    /// 10 minutes.
    ///
    /// Un avis publié invalide la clé immédiatement. Le TTL n'est là que pour borner
    /// le pire cas — et personne n'a jamais été lésé parce qu'une moyenne est passée
    /// de 4,3 à 4,4 avec dix minutes de retard.
    /// </summary>
    public static readonly TimeSpan RatingTtl = TimeSpan.FromMinutes(10);
}
