using FluentAssertions;
using Marketplace.Modules.Loyalty.Domain.Accounts;
using Xunit;

namespace Loyalty.UnitTests;

public class LoyaltyAccountTests
{
    private static LoyaltyAccount NewAccount() => LoyaltyAccount.Create(Guid.NewGuid()).Value;

    [Fact]
    public void New_account_is_bronze_with_zero_balance()
    {
        var account = NewAccount();

        account.PointsBalance.Should().Be(0);
        account.Tier.Should().Be(LoyaltyTier.Bronze);
    }

    [Fact]
    public void Earning_points_raises_tier()
    {
        var account = NewAccount();

        account.EarnPoints(1200, PointReason.Purchase, Guid.NewGuid());
        account.Tier.Should().Be(LoyaltyTier.Silver);

        account.EarnPoints(4000, PointReason.Purchase, Guid.NewGuid());
        account.Tier.Should().Be(LoyaltyTier.Gold);
        account.PointsBalance.Should().Be(5200);
    }

    [Fact]
    public void Redeem_reduces_balance_but_not_tier()
    {
        var account = NewAccount();
        account.EarnPoints(5000, PointReason.Purchase, Guid.NewGuid());

        account.RedeemPoints(3000).IsSuccess.Should().BeTrue();

        account.PointsBalance.Should().Be(2000);
        account.Tier.Should().Be(LoyaltyTier.Gold); // palier basé sur le cumul, pas le solde
    }

    [Fact]
    public void Redeem_more_than_balance_fails()
        => NewAccount().RedeemPoints(100).IsFailure.Should().BeTrue();

    [Fact]
    public void HasEarnedForOrder_is_true_after_purchase_credit()
    {
        var account = NewAccount();
        var orderId = Guid.NewGuid();
        account.EarnPoints(50, PointReason.Purchase, orderId);

        account.HasEarnedForOrder(orderId).Should().BeTrue();
        account.HasEarnedForOrder(Guid.NewGuid()).Should().BeFalse();
    }
}
