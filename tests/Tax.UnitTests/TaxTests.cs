using FluentAssertions;
using Marketplace.Modules.Tax.Domain.Taxes;
using Xunit;

namespace Tax.UnitTests;

public class TaxTests
{
    private static readonly DateTime Now = new(2026, 6, 24, 0, 0, 0, DateTimeKind.Utc);

    private static TaxRule Rule(string jur, Guid? cat, decimal rate, TaxType type = TaxType.Vat)
        => TaxRule.Create(jur, cat, rate, type, Now.AddDays(-1)).Value;

    [Fact]
    public void Vat_computes_amount_times_rate()
        => Rule("BJ", null, 0.18m).ComputeTax(10000m).Should().Be(1800m);

    [Fact]
    public void Exempt_rule_computes_zero()
        => Rule("BJ", null, 0m, TaxType.Exempt).ComputeTax(10000m).Should().Be(0m);

    [Fact]
    public void Resolver_prefers_category_specific_rule()
    {
        var category = Guid.NewGuid();
        var jurisdictionWide = Rule("BJ", null, 0.18m);
        var categoryRule = Rule("BJ", category, 0.05m);

        var resolved = TaxResolver.Resolve(new[] { jurisdictionWide, categoryRule }, "bj", category, Now);

        resolved.Should().Be(categoryRule);
        resolved!.ComputeTax(10000m).Should().Be(500m);
    }

    [Fact]
    public void Resolver_returns_null_for_unknown_jurisdiction()
        => TaxResolver.Resolve(new[] { Rule("BJ", null, 0.18m) }, "FR", Guid.NewGuid(), Now).Should().BeNull();
}
