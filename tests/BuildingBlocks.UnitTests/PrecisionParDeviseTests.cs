using FluentAssertions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Xunit;

namespace BuildingBlocks.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// UN FRANC CFA N'A PAS DE CENTIMES, ET C'EST « Money » QUI LE SAIT.
///
/// <c>Money.Create</c> arrondissait TOUT à deux décimales. En XOF — devise sans
/// subdivision — cela fabriquait des montants comme 8 999,10 : acceptés par le
/// domaine, transportés jusqu'au total de commande, puis tronqués à l'entier par la
/// passerelle. La commande était enregistrée et marquée PAYÉE pour 8 999,10 alors que
/// le prestataire n'avait encaissé que 8 999.
///
/// Ces tests gardent la règle à sa source. Ils ne vérifient pas un calcul : ils
/// vérifient qu'aucun montant impossible ne peut plus ENTRER dans le système.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PrecisionParDeviseTests
{
    // ── La table de décimales ───────────────────────────────────────────────────

    [Theory]
    [InlineData("XOF", 0)]
    [InlineData("XAF", 0)]
    [InlineData("JPY", 0)]
    [InlineData("GNF", 0)]
    [InlineData("EUR", 2)]
    [InlineData("USD", 2)]
    [InlineData("GBP", 2)]
    public void Chaque_devise_connue_declare_sa_precision(string devise, int decimales)
        => Money.DecimalsFor(devise).Should().Be(decimales);

    [Fact]
    public void La_table_ignore_la_casse_et_les_espaces()
    {
        // Money.Zero ne normalise pas la devise, et rien n'oblige un appelant à écrire
        // en majuscules : la précision ne peut pas dépendre de la frappe.
        Money.DecimalsFor("xof").Should().Be(0);
        Money.DecimalsFor(" Eur ").Should().Be(2);
    }

    [Fact]
    public void Une_devise_inconnue_retombe_sur_deux_decimales()
    {
        // LE DÉFAUT EST ASYMÉTRIQUE, PAS ARBITRAIRE. Sous-arrondir une devise à
        // centimes détruirait de la valeur réelle sur un montant légitime ; sur-arrondir
        // une devise sans subdivision ne peut plus produire d'encaissement faux, les
        // passerelles refusant désormais toute devise qu'elles ne savent pas traiter.
        // S'y ajoute le volume : la liste des devises d'exposant 0 est fermée et courte.
        Money.DecimalsFor("ZZZ").Should().Be(Money.UnknownCurrencyDecimals);
        Money.UnknownCurrencyDecimals.Should().Be(2);
    }

    [Theory]
    [InlineData("XOF", true)]
    [InlineData("KRW", true)]
    [InlineData("EUR", false)]
    [InlineData("ZZZ", false)]
    public void IsZeroDecimal_repond_pour_les_appelants_qui_convertissent(string devise, bool sansSubdivision)
        => Money.IsZeroDecimal(devise).Should().Be(sansSubdivision);

    // ── Aucun centime ne peut entrer en XOF ─────────────────────────────────────

    [Fact]
    public void Un_montant_en_franc_cfa_est_toujours_entier()
    {
        Money.Create(8_999.10m, "XOF").Value.Amount.Should().Be(8_999m);
        Money.Create(8_999.60m, "XOF").Value.Amount.Should().Be(9_000m);
        Money.Create(0.40m, "XOF").Value.Amount.Should().Be(0m);
        Money.Create(228.81m, "XOF").Value.Amount.Should().Be(229m);
    }

    [Fact]
    public void Le_cas_qui_a_produit_le_defaut()
    {
        // 10 % de 89 991 valaient 8 999,10 : un dixième de franc que personne ne
        // refusait, et que la passerelle finissait par jeter en silence.
        var remise = Money.Create(89_991m * 10m / 100m, "XOF").Value;

        remise.Amount.Should().Be(8_999m);
        (remise.Amount % 1m).Should().Be(0m);
    }

    [Fact]
    public void Une_devise_a_centimes_garde_ses_centimes()
    {
        // La correction ne doit PAS raboter l'euro : arrondir une devise à centimes à
        // l'unité détruirait de la valeur réelle sur une ligne parfaitement légitime.
        Money.Create(8_999.10m, "EUR").Value.Amount.Should().Be(8_999.10m);
        Money.Create(1_234.567m, "EUR").Value.Amount.Should().Be(1_234.57m);
    }

    [Fact]
    public void L_arrondi_est_expose_pour_tous_les_calculs_monetaires_du_depot()
    {
        // C'est cette primitive que doivent appeler remises, taxes, commissions et
        // répartitions, à la place d'un « decimal.Round(x, 2) » écrit en dur.
        Money.Round(1.4m, "XOF").Should().Be(1m);
        Money.Round(1.6m, "XOF").Should().Be(2m);
        Money.Round(1.4444m, "EUR").Should().Be(1.44m);
    }

    // ── L'affichage suit la devise ──────────────────────────────────────────────

    [Fact]
    public void L_affichage_n_invente_plus_deux_decimales_en_franc_cfa()
    {
        // Le format était figé à « 0.00 » : « 8999.00 XOF » laissait croire à une
        // précision que la caisse ne connaît pas.
        Money.Create(8_999m, "XOF").Value.ToString().Should().Be("8999 XOF");
        Money.Create(50m, "EUR").Value.ToString().Should().Be("50.00 EUR");
    }

    // ── Les garde-fous existants ne bougent pas ─────────────────────────────────

    [Fact]
    public void Un_montant_negatif_ou_une_devise_mal_formee_restent_refuses()
    {
        Money.Create(-1m, "XOF").IsFailure.Should().BeTrue();
        Money.Create(10m, "XO").IsFailure.Should().BeTrue();
        Money.Create(10m, "  ").IsFailure.Should().BeTrue();
    }
}
