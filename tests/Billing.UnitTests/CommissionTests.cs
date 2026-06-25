using FluentAssertions;
using Marketplace.Modules.Billing.Domain.Commissions;
using Xunit;

namespace Billing.UnitTests;

public class CommissionTests
{
    private static readonly DateTime Now = new(2026, 6, 24, 0, 0, 0, DateTimeKind.Utc);

    private static CommissionRule Rule(CommissionScope scope, Guid? target, decimal rate, decimal fixedFee = 0m, decimal? min = null, decimal? max = null)
    {
        var r = CommissionRule.Create(scope, target, rate, fixedFee, "XOF", min, max, Now.AddDays(-1)).Value;
        return r;
    }

    [Fact]
    public void ComputeCommission_matches_dossier_example()
    {
        // Revenu vendeur brut = base - réduction vendeur = 5600 ; commission 10% = 560.
        var rule = Rule(CommissionScope.Global, null, 0.10m);

        rule.ComputeCommission(5600m).Should().Be(560m);
    }

    [Fact]
    public void Resolver_prefers_most_specific_rule()
    {
        var seller = Guid.NewGuid();
        var category = Guid.NewGuid();
        var global = Rule(CommissionScope.Global, null, 0.10m);
        var cat = Rule(CommissionScope.Category, category, 0.08m);
        var sel = Rule(CommissionScope.Seller, seller, 0.05m);

        var resolved = CommissionResolver.Resolve(new[] { global, cat, sel }, seller, category, Now);

        resolved.Should().Be(sel);
        resolved!.ComputeCommission(10000m).Should().Be(500m);
    }

    [Fact]
    public void Resolver_falls_back_to_global_when_no_specific_rule()
    {
        var global = Rule(CommissionScope.Global, null, 0.10m);

        var resolved = CommissionResolver.Resolve(new[] { global }, Guid.NewGuid(), Guid.NewGuid(), Now);

        resolved.Should().Be(global);
    }

    [Fact]
    public void Commission_is_clamped_between_min_and_max()
    {
        var rule = Rule(CommissionScope.Global, null, 0.10m, fixedFee: 0m, min: 100m, max: 500m);

        rule.ComputeCommission(500m).Should().Be(100m);   // 50 -> min 100
        rule.ComputeCommission(10000m).Should().Be(500m); // 1000 -> max 500
    }

    [Fact]
    public void Rate_above_one_is_rejected()
        => CommissionRule.Create(CommissionScope.Global, null, 1.5m, 0m, "XOF", null, null, Now).IsFailure.Should().BeTrue();
}
