using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>Frontières de l'après-vente : Returns (RMA) et Disputes.</summary>
public class AfterSaleModulesBoundaryTests
{
    private static readonly Assembly ReturnsDomain =
        typeof(Marketplace.Modules.Returns.Domain.Returns.ReturnRequest).Assembly;

    private static readonly Assembly ReturnsApplication =
        typeof(Marketplace.Modules.Returns.Application.Returns.RequestReturnCommand).Assembly;

    private static readonly Assembly DisputesDomain =
        typeof(Marketplace.Modules.Disputes.Domain.Disputes.Dispute).Assembly;

    private static readonly Assembly DisputesApplication =
        typeof(Marketplace.Modules.Disputes.Application.Disputes.OpenDisputeCommand).Assembly;

    [Fact]
    public void Domains_ne_dependent_pas_de_EFCore()
    {
        AssertNoEf(ReturnsDomain);
        AssertNoEf(DisputesDomain);
    }

    [Fact]
    public void Applications_ne_dependent_ni_de_EFCore_ni_de_leur_Infrastructure()
    {
        Types.InAssembly(ReturnsApplication).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Returns.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(DisputesApplication).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Disputes.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Applications_ne_touchent_que_les_Contracts_dOrdering()
    {
        var forbidden = new[]
        {
            "Marketplace.Modules.Ordering.Domain",
            "Marketplace.Modules.Ordering.Application",
            "Marketplace.Modules.Ordering.Infrastructure"
        };

        Types.InAssembly(ReturnsApplication).ShouldNot().HaveDependencyOnAny(forbidden).GetResult().IsSuccessful.Should().BeTrue();
        Types.InAssembly(DisputesApplication).ShouldNot().HaveDependencyOnAny(forbidden).GetResult().IsSuccessful.Should().BeTrue();
    }

    private static void AssertNoEf(Assembly assembly)
        => Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult().IsSuccessful.Should().BeTrue();
}
