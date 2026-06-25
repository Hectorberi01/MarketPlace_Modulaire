using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>Frontières des modules Offers et Inventory.</summary>
public class OffersInventoryBoundaryTests
{
    private static readonly Assembly OffersDomain =
        typeof(Marketplace.Modules.Offers.Domain.Offers.Offer).Assembly;

    private static readonly Assembly OffersApplication =
        typeof(Marketplace.Modules.Offers.Application.Offers.Commands.CreateOffer.CreateOfferCommand).Assembly;

    private static readonly Assembly InventoryDomain =
        typeof(Marketplace.Modules.Inventory.Domain.Stock.InventoryItem).Assembly;

    private static readonly Assembly InventoryApplication =
        typeof(Marketplace.Modules.Inventory.Application.Stock.Commands.ReserveStockCommand).Assembly;

    [Fact]
    public void Offers_et_Inventory_Domain_ne_dependent_pas_de_EFCore()
    {
        AssertNoEf(OffersDomain);
        AssertNoEf(InventoryDomain);
    }

    [Fact]
    public void Offers_Application_ne_depend_que_des_Contracts_des_autres_modules()
    {
        var result = Types.InAssembly(OffersApplication)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Marketplace.Modules.Catalog.Domain",
                "Marketplace.Modules.Catalog.Application",
                "Marketplace.Modules.Catalog.Infrastructure",
                "Marketplace.Modules.Sellers.Domain",
                "Marketplace.Modules.Sellers.Application",
                "Marketplace.Modules.Sellers.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failure(result));
    }

    [Fact]
    public void Applications_ne_dependent_pas_de_EFCore_ni_de_leur_Infrastructure()
    {
        Types.InAssembly(OffersApplication).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Offers.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(InventoryApplication).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Inventory.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    private static void AssertNoEf(Assembly assembly)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failure(result));
    }

    private static string Failure(TestResult result)
        => $"Frontière violée par : {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}";
}
