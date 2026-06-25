using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>Garde-fou des frontières du module Identity (même règles que Catalog).</summary>
public class IdentityBoundaryTests
{
    private static readonly Assembly Domain =
        typeof(Marketplace.Modules.Identity.Domain.Users.User).Assembly;

    private static readonly Assembly Application =
        typeof(Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser.RegisterUserCommand).Assembly;

    private static readonly Assembly Contracts =
        typeof(Marketplace.Modules.Identity.Contracts.IIdentityModuleApi).Assembly;

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
    public void Application_ne_doit_dependre_ni_de_EFCore_ni_de_Infrastructure()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Marketplace.Modules.Identity.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failure(result));
    }

    [Fact]
    public void Contracts_ne_doit_exposer_aucun_interne()
    {
        var result = Types.InAssembly(Contracts)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Marketplace.Modules.Identity.Domain",
                "Marketplace.Modules.Identity.Application",
                "Marketplace.Modules.Identity.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failure(result));
    }

    private static string Failure(TestResult result)
        => $"Frontière violée par : {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}";
}
