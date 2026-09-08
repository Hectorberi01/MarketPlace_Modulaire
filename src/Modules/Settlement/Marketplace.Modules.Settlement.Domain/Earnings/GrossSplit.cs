namespace Marketplace.Modules.Settlement.Domain.Earnings;

/// <summary>
/// Répartition du brut d'une ligne entre commission plateforme, frais provider et net
/// vendeur — et son inverse, la contre-passation au prorata.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE CALCUL VIT DANS LE DOMAINE, ET PLUS DANS LES HANDLERS.
///
/// Il était écrit DEUX FOIS, à l'identique : une fois dans l'accrual, une fois dans la
/// contre-passation d'un remboursement. Deux copies d'une règle qui doit tomber juste au
/// franc près — et rien ne garantissait qu'elles resteraient d'accord. Une règle qui ne
/// peut pas être testée seule n'est pas une règle, c'est une intention.
///
/// L'INVARIANT, unique et non négociable :
///     brut = commission + frais provider + net vendeur
/// exactement, sans reste. C'est pourquoi le net est un RESTE et non un troisième
/// arrondi : trois arrondis indépendants ne se rejoignent pas, et l'écart s'accumule
/// silencieusement jusqu'à ce que les comptes cessent de tomber juste.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class GrossSplit
{
    /// <summary>
    /// Décompose un brut selon le barème AVEC LEQUEL IL A ÉTÉ CONSTRUIT.
    ///
    /// L'offre a bâti le prix acheteur comme <c>net × (1 + commission + provider)</c>.
    /// On refait le chemin à l'envers. Les taux doivent donc être ceux figés sur la
    /// ligne de commande, jamais ceux lus dans la configuration courante : appliquer
    /// un barème d'aujourd'hui à un prix calculé hier fait toucher au vendeur un net
    /// différent de celui qu'il a fixé.
    /// </summary>
    public static (decimal Commission, decimal ProviderFee, decimal Net) Split(
        decimal gross, decimal commissionRate, decimal providerFeeRate)
    {
        if (gross <= 0m)
        {
            return (0m, 0m, 0m);
        }

        var total = commissionRate + providerFeeRate;

        var commission = Math.Round(gross * commissionRate / (1 + total));
        var providerFee = Math.Round(gross * providerFeeRate / (1 + total));

        return (commission, providerFee, gross - commission - providerFee);
    }

    /// <summary>
    /// Contre-passe une fraction d'une écriture DÉJÀ COMPTABILISÉE.
    ///
    /// Aucun taux n'intervient : on reprend la même proportion de chaque part que celle
    /// qui a été créditée. C'est ce qui rend la contre-passation exacte même si le
    /// barème a changé entre la vente et le retour — recalculer à partir de taux
    /// reprendrait au vendeur un montant qui ne correspond à rien de ce qu'il a reçu.
    /// </summary>
    public static (decimal Commission, decimal ProviderFee, decimal Net) Prorate(
        decimal refundAmount, decimal accruedGross, decimal accruedCommission, decimal accruedProviderFee)
    {
        if (refundAmount <= 0m || accruedGross <= 0m)
        {
            return (0m, 0m, 0m);
        }

        // Un remboursement ne peut pas excéder ce qui a été comptabilisé.
        var ratio = Math.Min(refundAmount / accruedGross, 1m);

        var commission = Math.Round(accruedCommission * ratio);
        var providerFee = Math.Round(accruedProviderFee * ratio);

        return (commission, providerFee, refundAmount - commission - providerFee);
    }
}
