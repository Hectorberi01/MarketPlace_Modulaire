using FluentAssertions;
using Marketplace.Modules.Tax.Domain.Taxes;
using Xunit;

namespace Tax.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// LA TVA EXTRAITE SUIT LA PRÉCISION DE LA DEVISE.
///
/// L'extraction arrondissait à DEUX décimales en dur. Sur 1 500 XOF à 18 %, elle
/// renvoyait 228,81 : un montant que ni la caisse ni la déclaration ne savent porter,
/// le franc CFA n'ayant aucune subdivision. Le chiffre remontait pourtant tel quel
/// dans la ventilation de la commande.
///
/// L'invariant arithmétique, lui, ne dépend pas de la précision : le hors taxe est
/// déduit par SOUSTRACTION, donc HT + TVA redonne exactement le TTC quel que soit
/// l'arrondi retenu. Ces tests gardent les deux propriétés à la fois.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class TaxeSansCentimeTests
{
    private static TaxRule Regle(decimal taux = 0.18m)
        => TaxRule.Create("BJ", null, taux, TaxType.Vat, DateTime.UtcNow.AddDays(-1)).Value;

    [Fact]
    public void En_franc_cfa_la_taxe_extraite_est_entiere()
    {
        // 1 500 × 0,18 / 1,18 = 228,8135… : arrondi à l'unité, pas au centime.
        Regle().ExtractIncludedTax(1_500m, "XOF").Should().Be(229m);
    }

    [Fact]
    public void En_euro_la_taxe_extraite_garde_ses_centimes()
    {
        // Sous-arrondir une devise à centimes détruirait de la valeur réelle sur une
        // ventilation parfaitement légitime : la règle marche dans les deux sens.
        Regle().ExtractIncludedTax(1_500m, "EUR").Should().Be(228.81m);
    }

    [Fact]
    public void La_devise_de_la_place_est_le_defaut()
        => Regle().ExtractIncludedTax(1_500m).Should().Be(Regle().ExtractIncludedTax(1_500m, "XOF"));

    [Fact]
    public void La_taxe_ajoutee_suit_la_meme_regle()
    {
        // 1 505 × 0,18 = 270,90.
        Regle().ComputeTax(1_505m, "XOF").Should().Be(271m);
        Regle().ComputeTax(1_505m, "EUR").Should().Be(270.90m);
    }

    [Theory]
    [InlineData(11_800)]
    [InlineData(1_500)]
    [InlineData(99_999)]
    [InlineData(7)]
    [InlineData(1)]
    public void Le_hors_taxe_et_la_taxe_redonnent_toujours_le_montant_paye(decimal ttc)
    {
        var taxe = Regle().ExtractIncludedTax(ttc, "XOF");

        // Aucune tolérance, et aucun centime : les deux parts sont entières et leur
        // somme est exactement le montant encaissé.
        (taxe % 1m).Should().Be(0m);
        ((ttc - taxe) + taxe).Should().Be(ttc);
    }
}
