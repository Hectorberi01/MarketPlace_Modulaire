using FluentAssertions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.Modules.Pricing.Domain.Pricing;
using Marketplace.Modules.Pricing.Domain.Promotions;
using Xunit;

namespace Pricing.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// UNE REMISE EN FRANC CFA NE PRODUIT PAS DE CENTIMES.
///
/// Le calcul de remise arrondissait à DEUX décimales, quelle que soit la devise. En
/// XOF — qui n'a aucune subdivision — 10 % de 89 991 donnaient 8 999,10. Ce dixième de
/// franc n'était refusé par personne : il remontait dans le total de ligne, puis dans
/// le total de commande, jusqu'à la passerelle qui tronquait à l'entier. La commande
/// enregistrait 8 999,10 XOF, le prestataire encaissait 8 999, et la commande était
/// marquée payée pour un montant jamais perçu.
///
/// La précision se lit désormais sur la devise DU PRIX. Ces tests gardent les deux
/// côtés de la règle : plus de centimes en franc CFA, et pas de centimes rabotés en
/// euro — sous-arrondir une devise qui en a détruirait de la valeur réelle.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RemiseSansCentimeTests
{
    [Fact]
    public void Un_pourcentage_en_franc_cfa_donne_une_remise_entiere()
    {
        var promo = Pourcentage(10m);

        promo.ComputeDiscount(89_991m, "XOF").Should().Be(8_999m);
    }

    [Fact]
    public void Le_meme_pourcentage_en_euro_garde_ses_centimes()
    {
        var promo = Pourcentage(10m);

        promo.ComputeDiscount(89_991m, "EUR").Should().Be(8_999.10m);
    }

    [Fact]
    public void Le_calcul_de_ligne_transmet_la_devise_du_prix_de_base()
    {
        var promo = Pourcentage(10m);

        var breakdown = PriceCalculator.Calculate(
            Money.Create(89_991m, "XOF").Value, [promo], Contexte(89_991m)).Value;

        // Aucun centime nulle part : ni dans la remise, ni dans le prix final. C'est
        // ce total-là qui part au prestataire, et il doit être encaissable tel quel.
        breakdown.PlatformDiscount.Amount.Should().Be(8_999m);
        breakdown.FinalPrice.Amount.Should().Be(80_992m);
        (breakdown.FinalPrice.Amount % 1m).Should().Be(0m);
    }

    [Fact]
    public void Le_partage_d_une_remise_financee_a_deux_reste_entier_en_franc_cfa()
    {
        // 301 partagés moitié-moitié tombent sur 150,5. La part vendeur est arrondie
        // SUR LA GRILLE DE LA DEVISE, la part plateforme est le RESTE : leur somme
        // redonne exactement la remise, sans centime évaporé.
        var partagee = MontantFixe(301m, FundedBy.Shared);

        var (vendeur, plateforme) = PriceCalculator.CalculateCartLevel(
            301_000m, [partagee], Contexte(301_000m), "XOF");

        vendeur.Should().Be(150m);
        plateforme.Should().Be(151m);
        (vendeur + plateforme).Should().Be(301m);
    }

    // ── Montage ─────────────────────────────────────────────────────────────────

    private static PricingContext Contexte(decimal sousTotal)
        => new(
            ProductId: Guid.NewGuid(),
            CategoryId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            Quantity: 1,
            Subtotal: sousTotal,
            IsFirstOrder: false,
            Code: null,
            NowUtc: DateTime.UtcNow);

    private static Promotion Pourcentage(decimal valeur)
        => Creer(PromotionType.Percentage, valeur, FundedBy.Platform);

    private static Promotion MontantFixe(decimal valeur, FundedBy finance)
        => Creer(PromotionType.FixedAmount, valeur, finance);

    private static Promotion Creer(PromotionType type, decimal valeur, FundedBy finance)
    {
        var promotion = Promotion.Create(
            PromotionOwnerType.Platform,
            Guid.NewGuid(),
            finance,
            type,
            valeur,
            PromotionScopeType.Global,
            [],
            PromotionConditions.None,
            code: null,
            DateRange.Create(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)).Value,
            usageLimit: 0).Value;

        // Une promotion naît en Draft : sans activation, `IsActiveAt` la refuse et le
        // calcul ne prouverait plus rien sur l'arrondi.
        promotion.Activate();
        return promotion;
    }
}
