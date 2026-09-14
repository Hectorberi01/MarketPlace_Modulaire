namespace Marketplace.Modules.Offers.Application.Offers;

/// <summary>
/// Taux du modèle de prix « prix vendeur net + majoration plateforme ».
/// Le vendeur saisit son prix net ; la plateforme empile la commission et les frais
/// provider par-dessus pour obtenir le prix produit payé par l'acheteur.
/// Lié manuellement depuis la section de config « Pricing » dans l'installer
/// (même pattern que Payments/Billing : aucune dépendance IOptions).
///
/// NOTE : placé dans Offers.Application (et non Infrastructure) car le handler
/// l'injecte ; l'architecture impose Infrastructure -> Application, donc un type
/// d'Infrastructure ne peut pas être injecté dans un handler Application sans cycle.
/// L'installer (Infrastructure) le construit, le lie et l'enregistre en singleton.
/// </summary>
public sealed class PricingOptions
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LES DEUX TAUX SONT À ZÉRO, ET C'EST LE NOUVEAU MODÈLE.
    ///
    /// La plateforme ne se rémunère plus en majorant le prix de CHAQUE article : elle
    /// prélève un frais unique sur le total de la COMMANDE (voir
    /// `Ordering.PlatformFeeOptions`, 2 %). Le prix affiché est donc exactement celui
    /// que le vendeur a saisi — ce qui est aussi ce que le vendeur croyait déjà voir.
    ///
    /// LA MÉCANIQUE D'EMPILEMENT N'EST PAS SUPPRIMÉE POUR AUTANT. `ComputeBreakdown`
    /// reste en place et reste juste : à taux nuls, `productPrice = sellerPrice`. La
    /// garder coûte zéro et laisse rouvrir une majoration par article — par catégorie,
    /// par vendeur — sans reconstruire la tarification. La retirer aurait en revanche
    /// fallu réécrire l'agrégat Offer, ses quatre commandes, Settlement et la
    /// répartition du brut, pour un résultat arithmétiquement identique.
    ///
    /// CE QUE CELA CHANGE POUR SETTLEMENT : le brut d'une ligne vaut le prix vendeur,
    /// la commission et les frais provider valent zéro, et le vendeur perçoit donc
    /// 100 % de sa ligne. Aucune ligne de code à toucher là-bas : la répartition est
    /// une division par (1 + commission + provider), qui vaut 1.
    ///
    /// LES OFFRES DÉJÀ EN LIGNE ne se corrigent pas toutes seules — leur prix acheteur
    /// contient encore les 15 % d'hier. La migration
    /// `20260914_RetirerMajorationSurOffre` les réaligne : le prix vendeur est remonté
    /// au prix acheteur actuel, de sorte qu'AUCUN prix affiché ne bouge.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public decimal PlatformCommissionRate { get; set; } = 0m;

    /// <summary>Frais provider empilés par article. Zéro : voir l'encadré ci-dessus.</summary>
    public decimal ProviderFeeRate { get; set; } = 0m;
}
