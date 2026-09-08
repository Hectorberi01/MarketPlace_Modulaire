using FluentAssertions;
using Marketplace.Modules.Settlement.Domain.Earnings;
using Xunit;

namespace Settlement.UnitTests;

/// <summary>
/// Répartition du brut et contre-passation au prorata.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CES TESTS EXISTENT
///
/// La répartition relisait les taux depuis la CONFIGURATION, au moment de la
/// confirmation de commande. Or le prix payé par l'acheteur avait été construit par
/// l'offre avec les taux en vigueur le jour de la mise en vente.
///
/// Tant que personne ne touchait au barème, les deux coïncidaient et rien ne se voyait.
/// Le jour où la commission passait de 10 % à 12 %, TOUTES les offres déjà tarifées à
/// 10 % étaient réparties à 12 % : le vendeur touchait moins que le net qu'il avait
/// fixé, rétroactivement, sur l'ensemble de son catalogue, et sans la moindre trace
/// dans les journaux.
///
/// Le premier test ci-dessous est celui qui échouait avant la correction.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class GrossSplitTests
{
    /// <summary>
    /// LE TEST QUI MOTIVE TOUTE LA CORRECTION.
    ///
    /// Un vendeur fixe son net à 10 000. L'offre construit le prix acheteur à
    /// 10 000 × 1,15 = 11 500. Quel que soit le barème appliqué PLUS TARD à d'autres
    /// ventes, la répartition de CETTE ligne doit continuer à rendre 10 000 au vendeur,
    /// parce qu'elle est calculée avec les taux figés sur elle.
    /// </summary>
    [Fact]
    public void Le_bareme_fige_sur_la_ligne_rend_au_vendeur_le_net_qu_il_a_fixe()
    {
        const decimal netVoulu = 10_000m;
        const decimal commissionRate = 0.10m;
        const decimal providerFeeRate = 0.05m;

        // Ce que l'offre a construit à la mise en vente.
        var brut = netVoulu
                   + decimal.Round(netVoulu * commissionRate)
                   + decimal.Round(netVoulu * providerFeeRate);
        brut.Should().Be(11_500m);

        // Ce que Settlement en fait à la confirmation, avec les taux DE LA LIGNE.
        var (commission, providerFee, net) = GrossSplit.Split(brut, commissionRate, providerFeeRate);

        net.Should().Be(netVoulu, "le vendeur doit toucher exactement le net qu'il a fixé");
        commission.Should().Be(1_000m);
        providerFee.Should().Be(500m);
    }

    /// <summary>
    /// La démonstration du défaut, en négatif : appliquer un AUTRE barème au même brut
    /// lèse le vendeur. Ce test ne valide pas un comportement souhaitable — il fixe par
    /// écrit ce qui se produisait, pour que personne ne rebranche la configuration
    /// courante sur ce calcul en croyant que c'est équivalent.
    /// </summary>
    [Fact]
    public void Appliquer_un_bareme_different_de_celui_de_la_vente_lese_le_vendeur()
    {
        const decimal brut = 11_500m;                 // construit à 10 % + 5 %

        var (_, _, netAuBonBareme) = GrossSplit.Split(brut, 0.10m, 0.05m);
        var (_, _, netAuMauvaisBareme) = GrossSplit.Split(brut, 0.12m, 0.05m);

        netAuBonBareme.Should().Be(10_000m);
        netAuMauvaisBareme.Should().BeLessThan(netAuBonBareme,
            "c'est précisément la perte silencieuse que le figeage des taux supprime");
    }

    /// <summary>
    /// L'INVARIANT COMPTABLE : brut = commission + frais + net, au franc près.
    ///
    /// Le net est un RESTE, pas un troisième arrondi. Trois arrondis indépendants ne se
    /// rejoignent pas, et l'écart s'accumule jusqu'à ce que les comptes cessent de
    /// tomber juste.
    /// </summary>
    [Theory]
    [InlineData(10_000)]
    [InlineData(11_500)]
    [InlineData(3_333)]
    [InlineData(1_150)]
    [InlineData(7)]
    [InlineData(1)]
    public void La_repartition_tombe_juste_au_franc_pres(decimal brut)
    {
        var (commission, providerFee, net) = GrossSplit.Split(brut, 0.10m, 0.05m);

        (commission + providerFee + net).Should().Be(brut,
            "aucun franc ne doit se perdre en route");
        net.Should().BeGreaterThan(0m);
    }

    /// <summary>Un brut nul ou négatif ne produit aucune écriture.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_brut_non_positif_ne_produit_aucune_ecriture(decimal brut)
    {
        GrossSplit.Split(brut, 0.10m, 0.05m).Should().Be((0m, 0m, 0m));
    }

    /// <summary>
    /// La contre-passation défait EXACTEMENT l'écriture d'origine, sans jamais lire un
    /// taux — donc sans dépendre du barème du jour du retour.
    /// </summary>
    [Fact]
    public void La_contre_passation_totale_defait_exactement_l_ecriture_d_origine()
    {
        var (commission, providerFee, net) = GrossSplit.Split(11_500m, 0.10m, 0.05m);

        var reversed = GrossSplit.Prorate(11_500m, 11_500m, commission, providerFee);

        reversed.Commission.Should().Be(commission);
        reversed.ProviderFee.Should().Be(providerFee);
        reversed.Net.Should().Be(net);
    }

    /// <summary>
    /// Remboursement partiel : chaque part est reprise dans la même proportion, et la
    /// somme reste exactement le montant remboursé.
    /// </summary>
    [Theory]
    [InlineData(40)]
    [InlineData(50)]
    [InlineData(1)]
    public void Une_contre_passation_partielle_reprend_la_meme_proportion_de_chaque_part(int pourcentage)
    {
        var (commission, providerFee, _) = GrossSplit.Split(11_500m, 0.10m, 0.05m);
        var rembourse = decimal.Round(11_500m * pourcentage / 100m);

        var (c, p, n) = GrossSplit.Prorate(rembourse, 11_500m, commission, providerFee);

        (c + p + n).Should().Be(rembourse, "la contre-passation doit tomber juste, comme l'accrual");
        c.Should().BeLessThan(commission);
        p.Should().BeLessOrEqualTo(providerFee);
    }

    /// <summary>
    /// Un remboursement supérieur au brut comptabilisé est plafonné : on ne peut pas
    /// reprendre plus que ce qui a été crédité.
    /// </summary>
    [Fact]
    public void Une_contre_passation_ne_peut_pas_exceder_l_ecriture_d_origine()
    {
        var (commission, providerFee, _) = GrossSplit.Split(11_500m, 0.10m, 0.05m);

        var (c, p, _) = GrossSplit.Prorate(99_999m, 11_500m, commission, providerFee);

        c.Should().Be(commission);
        p.Should().Be(providerFee);
    }

    /// <summary>Sans écriture d'origine, rien n'est contre-passé.</summary>
    [Fact]
    public void Sans_ecriture_d_origine_rien_n_est_contre_passe()
    {
        GrossSplit.Prorate(5_000m, 0m, 0m, 0m).Should().Be((0m, 0m, 0m));
    }
}
