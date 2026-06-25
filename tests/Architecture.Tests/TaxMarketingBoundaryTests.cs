using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>Frontières des modules Tax et Marketing.</summary>
public class TaxMarketingBoundaryTests
{
    private static readonly Assembly TaxDomain =
        typeof(Marketplace.Modules.Tax.Domain.Taxes.TaxRule).Assembly;

    private static readonly Assembly TaxApplication =
        typeof(Marketplace.Modules.Tax.Application.Taxes.CreateTaxRuleCommand).Assembly;

    private static readonly Assembly MarketingDomain =
        typeof(Marketplace.Modules.Marketing.Domain.Campaigns.Campaign).Assembly;

    private static readonly Assembly MarketingApplication =
        typeof(Marketplace.Modules.Marketing.Application.Campaigns.CreateCampaignCommand).Assembly;

    [Fact]
    public void Domains_ne_dependent_pas_de_EFCore()
    {
        AssertNoEf(TaxDomain);
        AssertNoEf(MarketingDomain);
    }

    [Fact]
    public void Applications_ne_dependent_ni_de_EFCore_ni_de_leur_Infrastructure()
    {
        Types.InAssembly(TaxApplication).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Tax.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(MarketingApplication).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Marketing.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    private static void AssertNoEf(Assembly assembly)
        => Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult().IsSuccessful.Should().BeTrue();
}
