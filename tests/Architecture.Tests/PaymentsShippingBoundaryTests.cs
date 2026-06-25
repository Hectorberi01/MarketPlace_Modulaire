using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>Frontières des modules Payments et Shipping.</summary>
public class PaymentsShippingBoundaryTests
{
    private static readonly Assembly PaymentsDomain =
        typeof(Marketplace.Modules.Payments.Domain.Payments.Payment).Assembly;

    private static readonly Assembly PaymentsApplication =
        typeof(Marketplace.Modules.Payments.Application.Payments.Commands.InitiatePayment.InitiatePaymentCommand).Assembly;

    private static readonly Assembly ShippingDomain =
        typeof(Marketplace.Modules.Shipping.Domain.Shipments.Shipment).Assembly;

    private static readonly Assembly ShippingApplication =
        typeof(Marketplace.Modules.Shipping.Application.Shipments.Commands.MarkShipmentShippedCommand).Assembly;

    [Fact]
    public void Domains_ne_dependent_pas_de_EFCore()
    {
        AssertNoEf(PaymentsDomain);
        AssertNoEf(ShippingDomain);
    }

    [Fact]
    public void Applications_ne_dependent_ni_de_EFCore_ni_de_leur_Infrastructure()
    {
        Types.InAssembly(PaymentsApplication).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Payments.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(ShippingApplication).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Shipping.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Applications_ne_touchent_que_les_Contracts_des_autres_modules()
    {
        var forbidden = new[]
        {
            "Marketplace.Modules.Ordering.Domain", "Marketplace.Modules.Ordering.Application", "Marketplace.Modules.Ordering.Infrastructure",
            "Marketplace.Modules.Inventory.Domain", "Marketplace.Modules.Inventory.Application", "Marketplace.Modules.Inventory.Infrastructure"
        };

        Types.InAssembly(PaymentsApplication).ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(ShippingApplication).ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    private static void AssertNoEf(Assembly assembly)
        => Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult().IsSuccessful.Should().BeTrue();
}
