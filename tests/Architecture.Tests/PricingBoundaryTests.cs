using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>Frontières du module Pricing &amp; Promotions.</summary>
public class PricingBoundaryTests
{
    private static readonly Assembly PricingDomain =
        typeof(Marketplace.Modules.Pricing.Domain.Promotions.Promotion).Assembly;

    private static readonly Assembly PricingApplication =
        typeof(Marketplace.Modules.Pricing.Application.Promotions.Commands.CreatePromotion.CreatePromotionCommand).Assembly;

    [Fact]
    public void Pricing_Domain_ne_depend_pas_de_EFCore()
    {
        var result = Types.InAssembly(PricingDomain)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failure(result));
    }

    [Fact]
    public void Pricing_Application_ne_depend_ni_de_EFCore_ni_de_son_Infrastructure()
    {
        var result = Types.InAssembly(PricingApplication)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Pricing.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failure(result));
    }

    [Fact]
    public void Pricing_Application_ne_depend_pas_des_internes_des_autres_modules()
    {
        var result = Types.InAssembly(PricingApplication)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Marketplace.Modules.Catalog.Domain",
                "Marketplace.Modules.Catalog.Application",
                "Marketplace.Modules.Catalog.Infrastructure",
                "Marketplace.Modules.Sellers.Domain",
                "Marketplace.Modules.Sellers.Application",
                "Marketplace.Modules.Offers.Domain",
                "Marketplace.Modules.Offers.Application")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failure(result));
    }

    private static string Failure(TestResult result)
        => $"Frontière violée par : {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}";
}
