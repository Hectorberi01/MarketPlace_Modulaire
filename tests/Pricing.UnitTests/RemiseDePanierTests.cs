using FluentAssertions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.Modules.Pricing.Domain.Pricing;
using Marketplace.Modules.Pricing.Domain.Promotions;
using Xunit;

namespace Pricing.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// UNE REMISE À MONTANT FIXE EST UNE REMISE D'ORDRE, PAS D'ARTICLE.
///
/// C'était le défaut le plus coûteux du parcours d'achat, et il se composait de deux
/// multiplications indépendantes. Le prix envoyé à Pricing était UNITAIRE, donc la
/// remise fixe s'appliquait par article ; puis l'appelant multipliait le résultat par
/// la quantité ; et la promotion étant de portée « panier », elle était déclarée
/// applicable à CHAQUE ligne. Un coupon de 500 sur deux lignes de trois articles en
/// retirait 3 000. Le décompte, lui, ne comptait qu'une utilisation : le plafond du
/// coupon ne protégeait de rien.
///
/// La séparation est désormais explicite : `Calculate` refuse les remises à montant
/// fixe, `CalculateCartLevel` ne traite qu'elles. Ces deux garanties sont
/// symétriques et il faut les deux — l'une sans l'autre perd une famille entière de
/// promotions ou la compte deux fois.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RemiseDePanierTests
{
    private static readonly Guid Boutique = Guid.NewGuid();

    // ── Le calcul de LIGNE ignore les remises à montant fixe ────────────────────

    [Fact]
    public void Le_calcul_de_ligne_ignore_une_remise_a_montant_fixe()
    {
        var coupon = CouponFixe(500m);

        var breakdown = PriceCalculator.Calculate(
            Money.Create(30_000m, "XOF").Value, [coupon], Contexte(cartSubtotal: 30_000m)).Value;

        // Sans cette exclusion, l'appel étant fait une fois par ligne, 500 seraient
        // retirés autant de fois qu'il y a de lignes.
        breakdown.PlatformDiscount.Amount.Should().Be(0m);
        breakdown.FinalPrice.Amount.Should().Be(30_000m);
    }

    [Fact]
    public void Le_calcul_de_ligne_applique_toujours_un_pourcentage()
    {
        // Un pourcentage est LINÉAIRE : appliqué ligne par ligne, il donne le même
        // résultat qu'appliqué au panier. Il n'a donc aucune raison d'être exclu.
        var promo = CouponPourcentage(10m);

        var breakdown = PriceCalculator.Calculate(
            Money.Create(30_000m, "XOF").Value, [promo], Contexte(cartSubtotal: 30_000m)).Value;

        breakdown.PlatformDiscount.Amount.Should().Be(3_000m);
        breakdown.FinalPrice.Amount.Should().Be(27_000m);
    }

    // ── Le calcul de PANIER ne traite qu'elles ──────────────────────────────────

    [Fact]
    public void La_remise_de_panier_est_retiree_une_seule_fois()
    {
        var coupon = CouponFixe(500m);

        var (seller, platform) = PriceCalculator.CalculateCartLevel(
            cartSubtotal: 30_000m, [coupon], Contexte(cartSubtotal: 30_000m));

        seller.Should().Be(0m);
        platform.Should().Be(500m);
    }

    [Fact]
    public void Le_calcul_de_panier_ignore_un_pourcentage()
    {
        // Sinon il serait compté deux fois : une fois par ligne, une fois ici.
        var promo = CouponPourcentage(10m);

        var (seller, platform) = PriceCalculator.CalculateCartLevel(
            cartSubtotal: 30_000m, [promo], Contexte(cartSubtotal: 30_000m));

        (seller + platform).Should().Be(0m);
    }

    [Fact]
    public void Une_remise_superieure_au_panier_est_plafonnee()
    {
        var coupon = CouponFixe(10_000m);

        var (seller, platform) = PriceCalculator.CalculateCartLevel(
            cartSubtotal: 3_000m, [coupon], Contexte(cartSubtotal: 3_000m));

        // Un coupon de 10 000 sur un panier de 3 000 rend 3 000, pas une dette de 7 000.
        (seller + platform).Should().Be(3_000m);
    }

    [Fact]
    public void Une_remise_fixe_ciblee_sur_un_produit_ne_s_applique_nulle_part()
    {
        // « −500 sur les chaussures » n'a pas de sens univoque : fixe par ligne ? par
        // panier ? L'interpréter au hasard coûterait de l'argent. Elle est donc écartée
        // des deux calculs, et c'est délibéré.
        var coupon = CouponFixe(500m, PromotionScopeType.Product, [Guid.NewGuid()]);

        var (seller, platform) = PriceCalculator.CalculateCartLevel(
            cartSubtotal: 30_000m, [coupon], Contexte(cartSubtotal: 30_000m));

        (seller + platform).Should().Be(0m);
    }

    // ── Le seuil porte sur le panier, pas sur la ligne ──────────────────────────

    [Fact]
    public void Un_seuil_de_montant_est_atteint_par_le_panier_entier()
    {
        var promo = CouponPourcentage(10m, new PromotionConditions(MinAmount: 50_000m));

        // Dix lignes à 10 000 : le panier fait 100 000, la ligne 10 000. Le seuil était
        // évalué sur la LIGNE, donc refusé sur un panier qui le franchit largement.
        var breakdown = PriceCalculator.Calculate(
            Money.Create(10_000m, "XOF").Value, [promo], Contexte(cartSubtotal: 100_000m)).Value;

        breakdown.PlatformDiscount.Amount.Should().Be(1_000m);
    }

    [Fact]
    public void Un_seuil_non_atteint_par_le_panier_refuse_la_promotion()
    {
        var promo = CouponPourcentage(10m, new PromotionConditions(MinAmount: 50_000m));

        var breakdown = PriceCalculator.Calculate(
            Money.Create(10_000m, "XOF").Value, [promo], Contexte(cartSubtotal: 30_000m)).Value;

        // La faute symétrique : une seule ligne au-dessus du seuil accordait la remise
        // sur TOUTES les lignes. Le seuil doit se lire sur le panier, dans les deux sens.
        breakdown.PlatformDiscount.Amount.Should().Be(0m);
    }

    // ── Montage ─────────────────────────────────────────────────────────────────

    private static PricingContext Contexte(decimal cartSubtotal, int totalQuantity = 6)
        => new(
            ProductId: Guid.NewGuid(),
            CategoryId: Guid.NewGuid(),
            SellerId: Boutique,
            Quantity: totalQuantity,
            Subtotal: cartSubtotal,
            IsFirstOrder: false,
            Code: null,
            NowUtc: DateTime.UtcNow);

    private static Promotion CouponFixe(
        decimal valeur,
        PromotionScopeType portee = PromotionScopeType.Cart,
        IEnumerable<Guid>? cibles = null)
        => Creer(PromotionType.FixedAmount, valeur, portee, cibles, PromotionConditions.None);

    private static Promotion CouponPourcentage(decimal valeur, PromotionConditions? conditions = null)
        => Creer(PromotionType.Percentage, valeur, PromotionScopeType.Global, null,
                 conditions ?? PromotionConditions.None);

    private static Promotion Creer(
        PromotionType type, decimal valeur, PromotionScopeType portee,
        IEnumerable<Guid>? cibles, PromotionConditions conditions)
    {
        var promotion = Promotion.Create(
            PromotionOwnerType.Platform,
            Guid.NewGuid(),
            FundedBy.Platform,
            type,
            valeur,
            portee,
            cibles ?? [],
            conditions,
            code: null,
            DateRange.Create(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)).Value,
            usageLimit: 0).Value;

        // ACTIVATION EXPLICITE. Une promotion naît en « Draft », et `IsActiveAt` exige
        // « Active » : sans cet appel, tous les tests ci-dessus passeraient au vert pour
        // la mauvaise raison — la promotion serait ignorée, pas correctement exclue.
        promotion.Activate();
        return promotion;
    }
}
