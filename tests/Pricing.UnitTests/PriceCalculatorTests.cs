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

    [Fact]
    public void Stacks_seller_and_platform_discounts_and_traces_funders()
    {
        // Scénario dossier : base 7000, -20% financé vendeur, -500 (code) financé plateforme.
        var basePrice = Money.Create(7000m, "XOF").Value;
        var sellerPercent = Promo(PromotionType.Percentage, 20m, FundedBy.Seller);
        var platformCode = Promo(PromotionType.FixedAmount, 500m, FundedBy.Platform, code: "WELCOME");

        var result = PriceCalculator.Calculate(
            basePrice, new[] { sellerPercent, platformCode }, Context(code: "WELCOME"));

        result.IsSuccess.Should().BeTrue();
        var b = result.Value;
        b.SellerDiscount.Amount.Should().Be(1400m);
        b.PlatformDiscount.Amount.Should().Be(500m);
        b.FinalPrice.Amount.Should().Be(5100m);
        b.BasePrice.Amount.Should().Be(7000m);
    }

    [Fact]
    public void Promotion_requiring_code_is_skipped_when_code_absent()
    {
        var basePrice = Money.Create(7000m, "XOF").Value;
        var sellerPercent = Promo(PromotionType.Percentage, 20m, FundedBy.Seller);
        var platformCode = Promo(PromotionType.FixedAmount, 500m, FundedBy.Platform, code: "WELCOME");

        var result = PriceCalculator.Calculate(
            basePrice, new[] { sellerPercent, platformCode }, Context(code: null));

        result.Value.SellerDiscount.Amount.Should().Be(1400m);
        result.Value.PlatformDiscount.Amount.Should().Be(0m);
        result.Value.FinalPrice.Amount.Should().Be(5600m);
    }

    [Fact]
    public void Shared_discount_is_split_between_seller_and_platform()
    {
        var basePrice = Money.Create(1000m, "XOF").Value;
        var shared = Promo(PromotionType.FixedAmount, 300m, FundedBy.Shared);

        var result = PriceCalculator.Calculate(basePrice, new[] { shared }, Context());

        result.Value.SellerDiscount.Amount.Should().Be(150m);
        result.Value.PlatformDiscount.Amount.Should().Be(150m);
        result.Value.FinalPrice.Amount.Should().Be(700m);
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
