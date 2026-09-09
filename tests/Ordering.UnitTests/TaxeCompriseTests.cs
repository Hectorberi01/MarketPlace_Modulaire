using FluentAssertions;
using Marketplace.Modules.Ordering.Domain.Orders;
using Marketplace.Modules.Tax.Domain.Taxes;
using Xunit;

namespace Ordering.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// LA TAXE EST DANS LE TOTAL, PAS EN PLUS.
///
/// Ces tests gardent un invariant qui ne se voit sur aucun écran : la ventilation
/// fiscale DÉCRIT le montant payé, elle ne le déplace pas. Le jour où quelqu'un
/// « corrigera » le calcul pour ajouter la TVA au total, aucune page ne cassera —
/// l'acheteur paiera simplement 18 % de trop. C'est ce fichier qui doit rougir.
///
/// Le second invariant est arithmétique : HT + TVA doit redonner le TTC AU CENTIME.
/// Il tient parce que le hors taxe est déduit par soustraction ; il tomberait si
/// quelqu'un le recalculait par une seconde division.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class TaxeCompriseTests
{
    private const decimal TauxTva = 0.18m;

    // ── L'extraction, côté règle fiscale ────────────────────────────────────────

    [Fact]
    public void Extraire_la_taxe_d_un_montant_ttc_n_est_pas_la_multiplier_par_le_taux()
    {
        var regle = ReglePourTva(TauxTva);

        // 11 800 TTC à 18 % contiennent 1 800 de TVA (10 000 HT).
        var extraite = regle.ExtractIncludedTax(11_800m);

        extraite.Should().Be(1_800m);

        // LE PIÈGE, EXPLICITÉ : la formule « à l'endroit » donnerait 2 124, soit
        // 324 de TVA déclarée en trop sur cette seule commande.
        regle.ComputeTax(11_800m).Should().Be(2_124m);
        extraite.Should().NotBe(regle.ComputeTax(11_800m));
    }

    [Theory]
    [InlineData(11_800)]
    [InlineData(1_500)]
    [InlineData(99_999)]
    [InlineData(7)]
    [InlineData(1)]
    public void Le_hors_taxe_et_la_taxe_redonnent_toujours_le_montant_paye(decimal ttc)
    {
        var regle = ReglePourTva(TauxTva);

        var taxe = regle.ExtractIncludedTax(ttc);
        var horsTaxe = ttc - taxe;

        // Aucune tolérance. Une comptabilité qui ne retombe pas sur le montant
        // encaissé n'est pas une comptabilité.
        (horsTaxe + taxe).Should().Be(ttc);
    }

    [Fact]
    public void Une_regle_exoneree_ne_preleve_rien()
    {
        var regle = TaxRule.Create("BJ", null, 0.18m, TaxType.Exempt, DateTime.UtcNow.AddDays(-1)).Value;

        regle.ExtractIncludedTax(11_800m).Should().Be(0m);
    }

    // ── L'imputation, côté commande ─────────────────────────────────────────────

    [Fact]
    public void La_ventilation_ne_deplace_ni_le_total_ni_le_montant_du()
    {
        var commande = CommandeAvecTotal(10_000m, fraisDeLivraison: 1_800m);
        var totalAvant = commande.GrandTotal;
        var duAvant = commande.AmountDue;

        commande.ApplyIncludedTax(1_800m, TauxTva, Guid.NewGuid());

        commande.GrandTotal.Should().Be(totalAvant);
        commande.AmountDue.Should().Be(duAvant);
        commande.TaxTotal.Should().Be(1_800m);
        commande.NetTotal.Should().Be(totalAvant - 1_800m);
    }

    [Fact]
    public void Le_hors_taxe_et_la_taxe_de_la_commande_redonnent_le_total()
    {
        var commande = CommandeAvecTotal(10_000m, fraisDeLivraison: 1_800m);

        commande.ApplyIncludedTax(1_800m, TauxTva, null);

        (commande.NetTotal + commande.TaxTotal).Should().Be(commande.GrandTotal);
    }

    [Fact]
    public void Aucune_regle_fiscale_donne_une_taxe_nulle_et_non_un_echec()
    {
        var commande = CommandeAvecTotal(10_000m, fraisDeLivraison: 1_800m);

        // C'est ce que renvoie le module quand aucune règle n'est enregistrée : la
        // place doit continuer à vendre.
        commande.ApplyIncludedTax(0m, 0m, null);

        commande.TaxTotal.Should().Be(0m);
        commande.NetTotal.Should().Be(commande.GrandTotal);
    }

    [Fact]
    public void Une_taxe_superieure_au_total_est_plafonnee_plutot_que_de_casser_la_commande()
    {
        var commande = CommandeAvecTotal(10_000m, fraisDeLivraison: 0m);

        // Barème mal saisi en administration : un taux aberrant ne doit pas produire
        // un hors taxe NÉGATIF qui traverserait la comptabilité sans bruit.
        commande.ApplyIncludedTax(999_999m, 9.99m, null);

        commande.TaxTotal.Should().Be(commande.GrandTotal);
        commande.NetTotal.Should().Be(0m);
        commande.NetTotal.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public void Une_taxe_negative_est_ramenee_a_zero()
    {
        var commande = CommandeAvecTotal(10_000m, fraisDeLivraison: 0m);

        commande.ApplyIncludedTax(-500m, TauxTva, null);

        commande.TaxTotal.Should().Be(0m);
    }

    [Fact]
    public void Un_recalcul_du_total_a_la_baisse_replafonne_la_taxe()
    {
        var commande = CommandeAvecTotal(10_000m, fraisDeLivraison: 5_000m);

        // Taxe volontairement énorme mais légitime au moment où elle est posée :
        // 12 000 sur un total de 15 000.
        commande.ApplyIncludedTax(12_000m, TauxTva, null);
        commande.TaxTotal.Should().Be(12_000m);

        // Les frais retombent : le total descend à 10 000, sous la taxe figée. Sans
        // re-plafonnement, NetTotal vaudrait -2 000 et traverserait la comptabilité
        // sans qu'aucune assertion ne le voie.
        commande.SetShippingFee(0m);

        commande.GrandTotal.Should().Be(10_000m);
        commande.TaxTotal.Should().Be(10_000m);
        commande.NetTotal.Should().Be(0m);
    }

    [Fact]
    public void Le_taux_est_fige_sur_la_commande()
    {
        var commande = CommandeAvecTotal(10_000m, fraisDeLivraison: 0m);
        var regleId = Guid.NewGuid();

        commande.ApplyIncludedTax(1_525m, TauxTva, regleId);

        // Sans le taux ni la règle stockés, une facture réimprimée après un
        // changement de barème serait invérifiable.
        commande.TaxRate.Should().Be(TauxTva);
        commande.TaxRuleId.Should().Be(regleId);
    }

    // ── Montage ─────────────────────────────────────────────────────────────────

    private static TaxRule ReglePourTva(decimal taux)
        => TaxRule.Create("BJ", null, taux, TaxType.Vat, DateTime.UtcNow.AddDays(-1)).Value;

    private static Order CommandeAvecTotal(decimal prixUnitaire, decimal fraisDeLivraison)
    {
        var draft = new OrderLineDraft(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SKU-1", Guid.NewGuid(),
            Quantity: 1,
            UnitBasePrice: prixUnitaire,
            SellerDiscount: 0m,
            PlatformDiscount: 0m,
            FinalUnitPrice: prixUnitaire,
            CommissionRate: 0.10m,
            ProviderFeeRate: 0.02m);

        var commande = Order.Create(Guid.NewGuid(), Guid.NewGuid(), "XOF", [draft], null).Value;
        commande.SetShippingFee(fraisDeLivraison);
        return commande;
    }
}
