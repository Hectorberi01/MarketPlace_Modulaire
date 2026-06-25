using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Garde-fou des frontières du module Sellers. Cas particulier intéressant :
/// Sellers.Application a le droit de référencer les CONTRACTS d'Identity (appel
/// in-process), mais jamais son Domain/Application/Infrastructure.
/// </summary>
public class SellersBoundaryTests
{
    private static readonly Assembly Domain =
        typeof(Marketplace.Modules.Sellers.Domain.Sellers.Seller).Assembly;

    private static readonly Assembly Application =
        typeof(Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller.RegisterSellerCommand).Assembly;

    private static readonly Assembly Contracts =
        typeof(Marketplace.Modules.Sellers.Contracts.ISellerModuleApi).Assembly;

    [Fact]
    public void Domain_ne_doit_pas_dependre_de_EFCore()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failure(result));
    }

    [Fact]
    public void Application_ne_doit_pas_dependre_des_internes_d_Identity()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Marketplace.Modules.Identity.Domain",
                "Marketplace.Modules.Identity.Application",
                "Marketplace.Modules.Identity.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failure(result));
    }

    [Fact]
    public void Application_ne_doit_dependre_ni_de_EFCore_ni_de_sa_propre_Infrastructure()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Marketplace.Modules.Sellers.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failure(result));
    }

    [Fact]
    public void Contracts_ne_doit_exposer_aucun_interne()
    {
        var result = Types.InAssembly(Contracts)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Marketplace.Modules.Sellers.Domain",
                "Marketplace.Modules.Sellers.Application",
                "Marketplace.Modules.Sellers.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failure(result));
    }

    private static string Failure(TestResult result)
        => $"Frontière violée par : {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}";
}
