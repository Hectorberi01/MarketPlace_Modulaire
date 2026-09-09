using FluentAssertions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.Modules.Pricing.Domain.Pricing;
using Marketplace.Modules.Pricing.Domain.Promotions;
using Xunit;

namespace Pricing.UnitTests;

public class PriceCalculatorTests
{
    private static readonly DateTime Now = new(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);

    private static Promotion Promo(
        PromotionType type, decimal value, FundedBy fundedBy, string? code = null)
    {
        var period = DateRange.Create(Now.AddDays(-1), Now.AddDays(1)).Value;
        var promotion = Promotion.Create(
            PromotionOwnerType.Platform, Guid.NewGuid(), fundedBy, type, value,
            PromotionScopeType.Global, Array.Empty<Guid>(), PromotionConditions.None,
            code, period, usageLimit: 0).Value;
        promotion.Activate();
        return promotion;
    }

    private static PricingContext Context(string? code = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Quantity: 1, Subtotal: 7000m,
            IsFirstOrder: true, Code: code, NowUtc: Now);

    /// <summary>
    /// Scénario dossier : base 7000, −20 % financé vendeur, −500 (code) financé
    /// plateforme.
    ///
    /// LE SCÉNARIO N'A PAS CHANGÉ, SA RÉPARTITION EN DEUX APPELS SI. La remise à
    /// montant fixe ne sort plus du calcul de LIGNE : appelée une fois par ligne, elle
    /// était retirée autant de fois qu'il y avait de lignes. Elle passe désormais par
    /// `CalculateCartLevel`, appelé une seule fois pour le panier. Le total final est
    /// identique — c'est bien le même scénario, correctement découpé.
    /// </summary>
    [Fact]
    public void Stacks_seller_and_platform_discounts_and_traces_funders()
    {
        var basePrice = Money.Create(7000m, "XOF").Value;
        var sellerPercent = Promo(PromotionType.Percentage, 20m, FundedBy.Seller);
        var platformCode = Promo(PromotionType.FixedAmount, 500m, FundedBy.Platform, code: "WELCOME");

        var promotions = new[] { sellerPercent, platformCode };
        var context = Context(code: "WELCOME");

        var line = PriceCalculator.Calculate(basePrice, promotions, context);

        line.IsSuccess.Should().BeTrue();
        line.Value.SellerDiscount.Amount.Should().Be(1400m);
        line.Value.PlatformDiscount.Amount.Should().Be(0m, "une remise fixe n'est plus calculée par ligne");
        line.Value.FinalPrice.Amount.Should().Be(5600m);
        line.Value.BasePrice.Amount.Should().Be(7000m);

        var (cartSeller, cartPlatform) = PriceCalculator.CalculateCartLevel(7000m, promotions, context);

        cartSeller.Should().Be(0m);
        cartPlatform.Should().Be(500m);

        // Le total reste celui du dossier : 7000 − 1400 − 500.
        (line.Value.FinalPrice.Amount - cartSeller - cartPlatform).Should().Be(5100m);
    }

    [Fact]
    public void Promotion_requiring_code_is_skipped_when_code_absent()
    {
        var basePrice = Money.Create(7000m, "XOF").Value;
        var sellerPercent = Promo(PromotionType.Percentage, 20m, FundedBy.Seller);
        var platformCode = Promo(PromotionType.FixedAmount, 500m, FundedBy.Platform, code: "WELCOME");

        var promotions = new[] { sellerPercent, platformCode };
        var context = Context(code: null);

        var result = PriceCalculator.Calculate(basePrice, promotions, context);

        result.Value.SellerDiscount.Amount.Should().Be(1400m);
        result.Value.PlatformDiscount.Amount.Should().Be(0m);
        result.Value.FinalPrice.Amount.Should().Be(5600m);

        // Le contrôle du code doit tenir des DEUX côtés : sans lui, une remise fixe
        // sous code serait accordée à tout le monde par le chemin panier.
        var (cartSeller, cartPlatform) = PriceCalculator.CalculateCartLevel(7000m, promotions, context);

        (cartSeller + cartPlatform).Should().Be(0m);
    }

    /// <summary>
    /// Le partage vendeur / plateforme d'une remise « Shared ». Le calcul a migré vers
    /// le chemin PANIER avec les remises à montant fixe ; la règle, elle, est
    /// inchangée — moitié-moitié, la plateforme prenant le centime impair.
    /// </summary>
    [Fact]
    public void Shared_discount_is_split_between_seller_and_platform()
    {
        var shared = Promo(PromotionType.FixedAmount, 300m, FundedBy.Shared);

        var (seller, platform) = PriceCalculator.CalculateCartLevel(1000m, new[] { shared }, Context());

        seller.Should().Be(150m);
        platform.Should().Be(150m);
        (1000m - seller - platform).Should().Be(700m);
    }

    [Fact]
    public void Inactive_promotion_is_ignored()
    {
        var basePrice = Money.Create(1000m, "XOF").Value;
        var paused = Promo(PromotionType.Percentage, 50m, FundedBy.Seller);
        paused.Pause();

        var result = PriceCalculator.Calculate(basePrice, new[] { paused }, Context());

        result.Value.FinalPrice.Amount.Should().Be(1000m);
        result.Value.SellerDiscount.Amount.Should().Be(0m);
    }
}
