using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Pricing.Domain.Promotions;

namespace Marketplace.Modules.Pricing.Domain.Pricing;

/// <summary>
/// Contexte d'achat servant à l'application des promotions.
///
/// <paramref name="Subtotal"/> et <paramref name="Quantity"/> alimentent les CONDITIONS
/// d'éligibilité et décrivent donc le PANIER, pas la ligne : un seuil « à partir de
/// 50 000 » porte sur ce que l'acheteur dépense, pas sur ce que pèse un article.
/// </summary>
public sealed record PricingContext(
    Guid ProductId,
    Guid CategoryId,
    Guid SellerId,
    int Quantity,
    decimal Subtotal,
    bool IsFirstOrder,
    string? Code,
    DateTime NowUtc);

/// <summary>
/// Service de domaine qui calcule le prix effectif : il empile les promotions
/// applicables sur le prix de base (jamais modifié) et trace le financeur de
/// chaque réduction (vendeur vs plateforme) dans un PriceBreakdown auditable.
/// </summary>
public static class PriceCalculator
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// PRIX D'UNE LIGNE. LES REMISES DE PANIER SONT VOLONTAIREMENT EXCLUES.
    ///
    /// Une remise à MONTANT FIXE ne peut pas être calculée ici : appelée une fois par
    /// ligne, elle serait retirée autant de fois qu'il y a de lignes. Un coupon de 500
    /// sur un panier de deux lignes en retirait 1 000 — et l'appelant multipliant
    /// ensuite par la quantité, 3 000 sur deux lignes de trois articles. Le décompte,
    /// lui, ne comptait qu'une utilisation : le plafond du coupon ne protégeait de rien.
    ///
    /// Ces remises-là passent par <see cref="CalculateCartLevel"/>, appelé UNE fois.
    /// C'est le TYPE qui décide, pas la portée : une remise en POURCENTAGE reste
    /// linéaire, donc juste ligne par ligne, quelle que soit sa portée.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static Result<PriceBreakdown> Calculate(
        Money basePrice,
        IReadOnlyList<Promotion> promotions,
        PricingContext context)
    {
        var effective = basePrice.Amount;
        var sellerDiscount = 0m;
        var platformDiscount = 0m;

        foreach (var promotion in promotions)
        {
            if (IsCartLevel(promotion))
            {
                continue;
            }

            if (!promotion.IsActiveAt(context.NowUtc))
            {
                continue;
            }

            if (!promotion.AppliesTo(
                    context.ProductId, context.CategoryId, context.SellerId,
                    context.Subtotal, context.Quantity, context.IsFirstOrder, context.Code))
            {
                continue;
            }

            // La remise est arrondie à la précision de la devise DU PRIX, pas à deux
            // décimales en dur : en XOF, un pourcentage produisait des centimes que
            // rien en aval ne savait encaisser.
            var discount = Math.Min(promotion.ComputeDiscount(effective, basePrice.Currency), effective);
            if (discount <= 0m)
            {
                continue;
            }

            switch (promotion.FundedBy)
            {
                case FundedBy.Seller:
                    sellerDiscount += discount;
                    break;
                case FundedBy.Platform:
                    platformDiscount += discount;
                    break;
                case FundedBy.Shared:
                    // Moitié-moitié : la part VENDEUR est arrondie, celle de la
                    // plateforme est le RESTE. Une seule des deux parts est calculée —
                    // c'est ce qui garantit que leur somme redonne exactement la remise,
                    // là où deux arrondis indépendants laisseraient une unité orpheline.
                    // L'arrondi suit la devise : sans cela, partager 301 XOF donnait
                    // « 150,50 » à chacun, deux montants qu'aucune caisse ne sait payer.
                    var sellerShare = Money.Round(discount / 2m, basePrice.Currency);
                    sellerDiscount += sellerShare;
                    platformDiscount += discount - sellerShare;
                    break;
            }

            effective -= discount;
        }

        return PriceBreakdown.Create(basePrice.Amount, sellerDiscount, platformDiscount, basePrice.Currency);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// REMISE DE PANIER : CALCULÉE UNE FOIS, SUR LE SOUS-TOTAL.
    ///
    /// Ne traite que les remises à montant fixe — les seules qui ne se répartissent pas
    /// d'elles-mêmes. Le résultat est une ventilation par financeur, à répartir sur les
    /// lignes par l'appelant : lui seul connaît les lignes, et peut donc faire de la
    /// dernière un reste plutôt que d'arrondir chacune dans son coin.
    ///
    /// La remise est PLAFONNÉE au sous-total : une commande ne peut pas devenir
    /// négative, et un coupon de 10 000 sur un panier de 3 000 rend 3 000, pas une
    /// dette de 7 000.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="currency">
    /// Devise du sous-total. Elle décide de la PRÉCISION des remises : zéro décimale en
    /// franc CFA, deux en euro. Le calcul arrondissait à deux décimales en dur, ce qui
    /// fabriquait en XOF des centimes qu'aucune passerelle ne sait encaisser. Le défaut
    /// vaut <see cref="Money.DefaultCurrency"/> — la devise de la place — pour les
    /// appelants qui n'en fournissent pas encore.
    /// </param>
    public static (decimal SellerDiscount, decimal PlatformDiscount) CalculateCartLevel(
        decimal cartSubtotal,
        IReadOnlyList<Promotion> promotions,
        PricingContext context,
        string currency = Money.DefaultCurrency)
    {
        if (cartSubtotal <= 0m)
        {
            return (0m, 0m);
        }

        var remaining = cartSubtotal;
        var sellerDiscount = 0m;
        var platformDiscount = 0m;

        foreach (var promotion in promotions)
        {
            if (!IsCartLevel(promotion) || !promotion.IsActiveAt(context.NowUtc))
            {
                continue;
            }

            // Les conditions portent sur le panier ; le produit, la catégorie et le
            // vendeur d'une remise d'ordre n'ont pas de sens — une remise à montant
            // fixe CIBLÉE sur un produit serait ambiguë (fixe par ligne ? par panier ?)
            // et n'est donc retenue que si sa portée est le panier ou la place entière.
            if (!promotion.AppliesTo(
                    context.ProductId, context.CategoryId, context.SellerId,
                    context.Subtotal, context.Quantity, context.IsFirstOrder, context.Code))
            {
                continue;
            }

            var discount = Math.Min(promotion.ComputeDiscount(remaining, currency), remaining);
            if (discount <= 0m)
            {
                continue;
            }

            switch (promotion.FundedBy)
            {
                case FundedBy.Seller:
                    sellerDiscount += discount;
                    break;
                case FundedBy.Platform:
                    platformDiscount += discount;
                    break;
                case FundedBy.Shared:
                    // Même règle que dans le calcul de ligne : la part vendeur est
                    // arrondie SUR LA GRILLE DE LA DEVISE, la part plateforme est le
                    // reste. Deux arrondis indépendants ne se rejoindraient pas.
                    var sellerShare = Money.Round(discount / 2m, currency);
                    sellerDiscount += sellerShare;
                    platformDiscount += discount - sellerShare;
                    break;
            }

            remaining -= discount;
        }

        return (sellerDiscount, platformDiscount);
    }

    /// <summary>
    /// Cette promotion doit-elle être calculée sur le PANIER plutôt que sur la ligne ?
    ///
    /// Le critère est le TYPE, et lui seul. Un montant fixe ne se répartit pas ; un
    /// pourcentage, si. Une remise fixe ciblée sur un produit ou une catégorie est
    /// écartée du calcul de ligne comme du calcul de panier — sa portée est vérifiée
    /// dans <see cref="CalculateCartLevel"/>, où elle ne passera que si elle vise le
    /// panier ou la place entière. C'est délibéré : « −500 sur les chaussures » n'a pas
    /// de sens univoque, et l'interpréter au hasard coûterait de l'argent.
    /// </summary>
    private static bool IsCartLevel(Promotion promotion)
        => promotion.Type == PromotionType.FixedAmount;
}
