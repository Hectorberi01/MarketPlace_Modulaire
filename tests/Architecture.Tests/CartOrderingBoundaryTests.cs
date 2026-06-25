using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>Frontières des modules Cart et Ordering (cœur transactionnel).</summary>
public class CartOrderingBoundaryTests
{
    private static readonly Assembly CartDomain =
        typeof(Marketplace.Modules.Cart.Domain.Carts.Cart).Assembly;

    private static readonly Assembly CartApplication =
        typeof(Marketplace.Modules.Cart.Application.Carts.Commands.AddItem.AddItemToCartCommand).Assembly;

    private static readonly Assembly OrderingDomain =
        typeof(Marketplace.Modules.Ordering.Domain.Orders.Order).Assembly;

    private static readonly Assembly OrderingApplication =
        typeof(Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder.PlaceOrderCommand).Assembly;

    [Fact]
    public void Domains_ne_dependent_pas_de_EFCore()
    {
        AssertNoEf(CartDomain);
        AssertNoEf(OrderingDomain);
    }

    [Fact]
    public void Applications_ne_dependent_ni_de_EFCore_ni_de_leur_Infrastructure()
    {
        Types.InAssembly(CartApplication).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Cart.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(OrderingApplication).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Ordering.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Cart_et_Ordering_Application_ne_touchent_que_les_Contracts_des_autres_modules()
    {
        var forbidden = new[]
        {
            "Marketplace.Modules.Catalog.Domain", "Marketplace.Modules.Catalog.Application", "Marketplace.Modules.Catalog.Infrastructure",
            "Marketplace.Modules.Offers.Domain", "Marketplace.Modules.Offers.Application", "Marketplace.Modules.Offers.Infrastructure",
            "Marketplace.Modules.Inventory.Domain", "Marketplace.Modules.Inventory.Application", "Marketplace.Modules.Inventory.Infrastructure",
            "Marketplace.Modules.Pricing.Domain", "Marketplace.Modules.Pricing.Application", "Marketplace.Modules.Pricing.Infrastructure"
        };

        Types.InAssembly(CartApplication).ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(OrderingApplication).ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    private static void AssertNoEf(Assembly assembly)
        => Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult().IsSuccessful.Should().BeTrue();
}
