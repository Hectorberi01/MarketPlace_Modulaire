using Marketplace.BuildingBlocks.Domain.Primitives;

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
    /// <param name="currency">
    /// Devise du brut. Elle décide de la PRÉCISION des deux parts arrondies : ce calcul
    /// arrondissait à l'unité en dur, ce qui est juste en franc CFA et faux partout
    /// ailleurs — en euro, il rabotait les centimes de la commission et les déversait
    /// dans le net vendeur, puisque le net est le reste. Le défaut vaut
    /// <see cref="Money.DefaultCurrency"/>, la devise de la place.
    /// </param>
    public static (decimal Commission, decimal ProviderFee, decimal Net) Split(
        decimal gross, decimal commissionRate, decimal providerFeeRate, string currency = Money.DefaultCurrency)
    {
        if (gross <= 0m)
        {
            return (0m, 0m, 0m);
        }

        var total = commissionRate + providerFeeRate;

        var commission = Money.Round(gross * commissionRate / (1 + total), currency);
        var providerFee = Money.Round(gross * providerFeeRate / (1 + total), currency);

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
    /// <param name="currency">
    /// Devise de l'écriture contre-passée : même rôle que dans <see cref="Split"/>.
    /// </param>
    public static (decimal Commission, decimal ProviderFee, decimal Net) Prorate(
        decimal refundAmount, decimal accruedGross, decimal accruedCommission, decimal accruedProviderFee,
        string currency = Money.DefaultCurrency)
    {
        if (refundAmount <= 0m || accruedGross <= 0m)
        {
            return (0m, 0m, 0m);
        }

        // Un remboursement ne peut pas excéder ce qui a été comptabilisé.
        var ratio = Math.Min(refundAmount / accruedGross, 1m);

        var commission = Money.Round(accruedCommission * ratio, currency);
        var providerFee = Money.Round(accruedProviderFee * ratio, currency);

        return (commission, providerFee, refundAmount - commission - providerFee);
    }
}
