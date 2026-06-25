using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>Frontières du cœur financier : Billing et Settlement.</summary>
public class FinancialModulesBoundaryTests
{
    private static readonly Assembly BillingDomain =
        typeof(Marketplace.Modules.Billing.Domain.Commissions.CommissionRule).Assembly;

    private static readonly Assembly BillingApplication =
        typeof(Marketplace.Modules.Billing.Application.Commissions.CreateCommissionRuleCommand).Assembly;

    private static readonly Assembly SettlementDomain =
        typeof(Marketplace.Modules.Settlement.Domain.Batches.SettlementBatch).Assembly;

    private static readonly Assembly SettlementApplication =
        typeof(Marketplace.Modules.Settlement.Application.Batches.RunSettlementCommand).Assembly;

    [Fact]
    public void Domains_ne_dependent_pas_de_EFCore()
    {
        AssertNoEf(BillingDomain);
        AssertNoEf(SettlementDomain);
    }

    [Fact]
    public void Applications_ne_dependent_ni_de_EFCore_ni_de_leur_Infrastructure()
    {
        Types.InAssembly(BillingApplication).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Billing.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(SettlementApplication).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Settlement.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Settlement_Application_ne_touche_que_les_Contracts_des_autres_modules()
    {
        var result = Types.InAssembly(SettlementApplication).ShouldNot()
            .HaveDependencyOnAny(
                "Marketplace.Modules.Ordering.Domain", "Marketplace.Modules.Ordering.Application", "Marketplace.Modules.Ordering.Infrastructure",
                "Marketplace.Modules.Catalog.Domain", "Marketplace.Modules.Catalog.Application", "Marketplace.Modules.Catalog.Infrastructure",
                "Marketplace.Modules.Billing.Domain", "Marketplace.Modules.Billing.Application", "Marketplace.Modules.Billing.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    private static void AssertNoEf(Assembly assembly)
        => Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult().IsSuccessful.Should().BeTrue();
}
