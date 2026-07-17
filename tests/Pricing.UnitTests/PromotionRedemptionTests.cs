using FluentAssertions;
using Marketplace.Modules.Pricing.Domain.Promotions;
using Xunit;

namespace Pricing.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// CES TESTS GARDENT LES DEUX PLAFONDS D'UN COUPON. AVANT, AUCUN DES DEUX N'EXISTAIT.
///
/// `Promotion.RecordUsage()` était écrit, testé, documenté — et n'avait AUCUN appelant
/// dans tout le dépôt. `UsedCount` restait donc à 0 pour l'éternité, la condition
/// `UsedCount &lt; UsageLimit` était toujours vraie, et une promo « 100 premiers clients »
/// servait le 100 001ᵉ. Le plafond était une décoration.
///
/// Et il n'y avait aucune notion de limite PAR PERSONNE : un coupon plafonné à 1 000
/// usages pouvait être consommé 1 000 fois par le même acheteur.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class PromotionRedemptionTests
{
    private static Promotion ActivePromotion(int usageLimit = 0, int perUserLimit = 0, string? code = "BIENVENUE10")
    {
        var promotion = Promotion.Create(
            PromotionOwnerType.Platform,
            Guid.NewGuid(),
            FundedBy.Platform,
            PromotionType.Percentage,
            10m,
            PromotionScopeType.Global,
            Array.Empty<Guid>(),
            PromotionConditions.None,
            code,
            DateRange.Create(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30)).Value,
            usageLimit,
            perUserLimit).Value;

        promotion.Activate();
        return promotion;
    }

    [Fact]
    public void Le_plafond_global_est_desormais_APPLIQUE()
    {
        var promotion = ActivePromotion(usageLimit: 2);

        promotion.Redeem(buyerPriorRedemptions: 0).IsSuccess.Should().BeTrue();
        promotion.Redeem(buyerPriorRedemptions: 0).IsSuccess.Should().BeTrue();

        var third = promotion.Redeem(buyerPriorRedemptions: 0);

        third.IsFailure.Should().BeTrue("le plafond global était jusqu'ici une décoration : UsedCount ne bougeait jamais");
        third.Error.Code.Should().Be("pricing.promotion.usage_exceeded");
        promotion.UsedCount.Should().Be(2);
    }

    [Fact]
    public void Le_plafond_PAR_ACHETEUR_est_applique_meme_si_le_plafond_global_est_loin()
    {
        // Le cas qui coûte de l'argent : 1 000 usages autorisés, mais 1 seul par personne.
        var promotion = ActivePromotion(usageLimit: 1000, perUserLimit: 1);

        promotion.Redeem(buyerPriorRedemptions: 0).IsSuccess.Should().BeTrue();

        var second = promotion.Redeem(buyerPriorRedemptions: 1);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("pricing.promotion.per_user_limit_reached");
        promotion.UsedCount.Should().Be(1, "une tentative refusée ne doit RIEN décompter");
    }

    [Fact]
    public void Un_autre_acheteur_reste_eligible_quand_le_premier_a_epuise_SA_limite()
    {
        var promotion = ActivePromotion(usageLimit: 1000, perUserLimit: 1);

        promotion.Redeem(buyerPriorRedemptions: 1).IsFailure.Should().BeTrue();   // acheteur A, déjà servi
        promotion.Redeem(buyerPriorRedemptions: 0).IsSuccess.Should().BeTrue();   // acheteur B, jamais servi
    }

    [Fact]
    public void Zero_signifie_illimite__pour_les_deux_plafonds()
    {
        var promotion = ActivePromotion(usageLimit: 0, perUserLimit: 0);

        for (var i = 0; i < 50; i++)
        {
            promotion.Redeem(buyerPriorRedemptions: i).IsSuccess.Should().BeTrue();
        }

        promotion.UsedCount.Should().Be(50);
    }

    [Fact]
    public void Une_promotion_en_pause_ne_peut_pas_etre_consommee()
    {
        var promotion = ActivePromotion();
        promotion.Pause();

        var result = promotion.Redeem(buyerPriorRedemptions: 0);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pricing.promotion.not_active");
        promotion.UsedCount.Should().Be(0);
    }

    [Fact]
    public void Redeem_incremente_TOUJOURS_UsedCount__ce_qui_arme_le_verrou_optimiste()
    {
        // Ce test protège une propriété non évidente mais VITALE.
        //
        // La ligne `promotions` porte un jeton de concurrence (xmin). Or un jeton n'est
        // évalué que dans la clause WHERE d'un UPDATE. Si un jour `Redeem()` cessait de
        // modifier une colonne du parent — par exemple si le compteur migrait vers une
        // table annexe — EF n'émettrait plus d'UPDATE, le verrou deviendrait INERTE, et
        // deux commandes pourraient consommer le dernier exemplaire d'un coupon en même
        // temps. Sans que rien ne casse visiblement.
        //
        // C'est exactement le piège rencontré sur InventoryItem.Reserve().
        var promotion = ActivePromotion(usageLimit: 10);
        var before = promotion.UsedCount;

        promotion.Redeem(buyerPriorRedemptions: 0).IsSuccess.Should().BeTrue();

        promotion.UsedCount.Should().Be(before + 1,
            "c'est cet incrément qui salit la ligne, force l'UPDATE, et fait jouer le jeton xmin");
    }

    [Fact]
    public void Le_code_est_normalise_en_MAJUSCULES_a_la_creation()
    {
        // Sans quoi « bienvenue10 » saisi par l'acheteur ne retrouverait jamais
        // « BIENVENUE10 » créé par l'admin. Le panier normalise symétriquement.
        var promotion = ActivePromotion(code: "  bienvenue10  ");

        promotion.Code.Should().Be("BIENVENUE10");
    }

    [Fact]
    public void Un_plafond_par_acheteur_negatif_est_refuse()
    {
        var result = Promotion.Create(
            PromotionOwnerType.Platform, Guid.NewGuid(), FundedBy.Platform,
            PromotionType.Percentage, 10m, PromotionScopeType.Global,
            Array.Empty<Guid>(), PromotionConditions.None, "X",
            DateRange.Create(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)).Value,
            usageLimit: 0, perUserLimit: -1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pricing.promotion.per_user_limit_invalid");
    }
}
