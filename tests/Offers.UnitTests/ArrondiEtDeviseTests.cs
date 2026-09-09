using FluentAssertions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Offers.Domain.Offers;
using Xunit;

namespace Offers.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// NON-RÉGRESSION §3.1 ET §3.5 — L'OFFRE ARRONDIT DANS SA DEVISE, ET SA DEVISE EXISTE.
///
/// Deux défauts distincts se renforçaient l'un l'autre :
///
/// 1. La ventilation (commission, frais, prix acheteur) était arrondie à DEUX
///    décimales en dur. Sur le franc CFA, qui n'en a aucune, l'offre publiait donc
///    une commission de 100,10 et un prix acheteur entier : les trois nombres
///    affichés ne s'additionnaient plus.
///
/// 2. Aucune règle ne portait sur la devise. Un code inconnu était accepté, et
///    `Money` lui prêtait deux décimales par défaut — ce qui rendait le premier
///    défaut invisible pour toute devise que la place ne connaît pas.
///
/// Les tests ci-dessous verrouillent les deux, plus le trou symétrique de
/// `ChangePrice`, qui n'avait aucun contrôle de précision.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public class ArrondiEtDeviseTests
{
    private const decimal CommRate = 0.10m;
    private const decimal ProvRate = 0.05m;

    private static Result<Offer> Creer(decimal sellerPrice, string currency)
        => Offer.Create(
            Guid.NewGuid(), Guid.NewGuid(), Sku.Create("SKU-001").Value,
            sellerPrice, CommRate, ProvRate, currency,
            OfferCondition.New, FulfillmentType.Fbs, Guid.NewGuid(), 2);

    // ─────────────────────────────────────────────────────────────────────────────
    // La devise
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Une_devise_inconnue_est_refusee()
    {
        var result = Creer(7000m, "ZZZ");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("offers.offer.currency_unsupported");
    }

    [Theory]
    [InlineData("XOF")]
    [InlineData("EUR")]
    [InlineData("xof")] // la table est insensible à la casse
    public void Une_devise_connue_est_acceptee(string currency)
        => Creer(1000m, currency).IsSuccess.Should().BeTrue();

    [Fact]
    public void Money_distingue_devise_connue_et_repli_a_deux_decimales()
    {
        // Le repli de `DecimalsFor` demeure : un calcul déjà engagé doit arrondir.
        Money.DecimalsFor("ZZZ").Should().Be(Money.UnknownCurrencyDecimals);

        // Mais il ne dit plus « connue » : c'est ce qui manquait en amont.
        Money.IsSupported("ZZZ").Should().BeFalse();
        Money.IsSupported(null).Should().BeFalse();
        Money.IsSupported("  ").Should().BeFalse();
        Money.IsSupported("xof").Should().BeTrue();

        Money.SupportedCurrencies.Should().Contain("XOF").And.NotContain("ZZZ");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // L'arrondi de la ventilation
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sur_une_devise_sans_decimale_la_ventilation_est_entiere_et_s_additionne()
    {
        // 1001 XOF : 10 % = 100,1 et 5 % = 50,05. Avant correction, la commission
        // valait 100,10 et les frais 50,05 pendant que le prix acheteur, lui, était
        // arrondi par `Money.Create` — l'addition affichée était fausse.
        var offer = Creer(1001m, "XOF").Value;

        offer.CommissionAmount.Should().Be(100m);
        offer.ProviderFeeAmount.Should().Be(50m);
        offer.BasePrice.Amount.Should().Be(1151m);

        // L'invariant qui compte : les trois nombres publiés s'additionnent.
        (offer.SellerPrice + offer.CommissionAmount + offer.ProviderFeeAmount)
            .Should().Be(offer.BasePrice.Amount);
    }

    [Fact]
    public void Sur_une_devise_a_deux_decimales_les_centimes_sont_conserves()
    {
        // 10,50 EUR : 10 % = 1,05 et 5 % = 0,525 -> 0,52 (arrondi au pair, inchangé).
        var offer = Creer(10.50m, "EUR").Value;

        offer.CommissionAmount.Should().Be(1.05m);
        offer.ProviderFeeAmount.Should().Be(0.52m);
        offer.BasePrice.Amount.Should().Be(12.07m);

        (offer.SellerPrice + offer.CommissionAmount + offer.ProviderFeeAmount)
            .Should().Be(offer.BasePrice.Amount);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // La précision du prix saisi
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Un_prix_a_decimales_est_refuse_sur_une_devise_qui_n_en_a_pas()
    {
        var result = Creer(1000.50m, "XOF");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("offers.offer.price_precision");
    }

    [Fact]
    public void Un_prix_a_trois_decimales_est_refuse_sur_une_devise_qui_en_a_deux()
    {
        var result = Creer(10.505m, "EUR");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("offers.offer.price_precision");
    }

    [Fact]
    public void ChangePrice_applique_le_meme_controle_de_precision()
    {
        // Le trou symétrique : refuser 1000,50 à la création et l'admettre ensuite
        // ne fermait rien du tout.
        var offer = Creer(7000m, "XOF").Value;

        var result = offer.ChangePrice(1000.50m, CommRate, ProvRate);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("offers.offer.price_precision");
        offer.SellerPrice.Should().Be(7000m); // le prix n'a pas bougé
    }

    [Fact]
    public void ChangePrice_arrondit_la_ventilation_dans_la_devise()
    {
        var offer = Creer(7000m, "XOF").Value;

        offer.ChangePrice(1001m, CommRate, ProvRate).IsSuccess.Should().BeTrue();

        offer.CommissionAmount.Should().Be(100m);
        offer.ProviderFeeAmount.Should().Be(50m);
        offer.BasePrice.Amount.Should().Be(1151m);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // La remise
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Une_remise_en_pourcentage_arrondit_dans_la_devise()
    {
        var offer = Creer(1001m, "XOF").Value;

        // 10 % de remise sur 1001 -> 900,9, arrondi à 901 : un prix vendeur XOF est
        // un entier, y compris quand c'est une remise qui le produit.
        offer.ApplyDiscount(DiscountType.Percentage, 10m, null, CommRate, ProvRate)
            .IsSuccess.Should().BeTrue();

        offer.SellerPrice.Should().Be(901m);
        offer.SellerPrice.Should().Be(decimal.Truncate(offer.SellerPrice));
        (offer.SellerPrice + offer.CommissionAmount + offer.ProviderFeeAmount)
            .Should().Be(offer.BasePrice.Amount);
    }
}
