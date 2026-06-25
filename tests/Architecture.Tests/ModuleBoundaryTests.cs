using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Garde-fou automatique des frontières (cf. dossier §8). Ces tests font échouer
/// le build si un module dérive vers le monolithe spaghetti.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly CatalogDomain =
        typeof(Marketplace.Modules.Catalog.Domain.Products.Product).Assembly;

    private static readonly Assembly CatalogApplication =
        typeof(Marketplace.Modules.Catalog.Application.Products.Commands.CreateProduct.CreateProductCommand).Assembly;

    private static readonly Assembly CatalogContracts =
        typeof(Marketplace.Modules.Catalog.Contracts.ICatalogModuleApi).Assembly;

    [Fact]
    public void Domain_ne_doit_pas_dependre_de_EFCore()
    {
        var result = Types.InAssembly(CatalogDomain)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BuildFailure(result));
    }

    [Fact]
    public void Domain_ne_doit_dependre_ni_de_Application_ni_de_Infrastructure()
    {
        var result = Types.InAssembly(CatalogDomain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Marketplace.Modules.Catalog.Application",
                "Marketplace.Modules.Catalog.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BuildFailure(result));
    }

    [Fact]
    public void Application_ne_doit_pas_dependre_de_EFCore()
    {
        // L'Application dépend d'abstractions (repositories), jamais d'EF Core.
        var result = Types.InAssembly(CatalogApplication)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BuildFailure(result));
    }

    [Fact]
    public void Application_ne_doit_pas_dependre_de_Infrastructure()
    {
        var result = Types.InAssembly(CatalogApplication)
            .ShouldNot()
            .HaveDependencyOn("Marketplace.Modules.Catalog.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BuildFailure(result));
    }

    [Fact]
    public void Contracts_ne_doit_exposer_aucun_interne_du_module()
    {
        // Contracts est le seul projet référençable de l'extérieur : il ne doit
        // tirer ni le Domain, ni l'Application, ni l'Infrastructure.
        var result = Types.InAssembly(CatalogContracts)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Marketplace.Modules.Catalog.Domain",
                "Marketplace.Modules.Catalog.Application",
                "Marketplace.Modules.Catalog.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BuildFailure(result));
    }

    private static string BuildFailure(TestResult result)
    {
        var offenders = result.FailingTypeNames is null
            ? string.Empty
            : string.Join(", ", result.FailingTypeNames);

        return $"Frontière violée par : {offenders}";
    }
}
