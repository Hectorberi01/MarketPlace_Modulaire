using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>Frontières des modules de support : Reviews, Search, Notifications.</summary>
public class SupportingModulesBoundaryTests
{
    private static readonly Assembly ReviewsDomain =
        typeof(Marketplace.Modules.Reviews.Domain.Reviews.Review).Assembly;

    private static readonly Assembly ReviewsApplication =
        typeof(Marketplace.Modules.Reviews.Application.Reviews.Commands.SubmitReview.SubmitReviewCommand).Assembly;

    private static readonly Assembly SearchDomain =
        typeof(Marketplace.Modules.Search.Domain.Documents.SearchDocument).Assembly;

    private static readonly Assembly SearchApplication =
        typeof(Marketplace.Modules.Search.Application.Search.SearchProductsQuery).Assembly;

    private static readonly Assembly NotificationsDomain =
        typeof(Marketplace.Modules.Notifications.Domain.Notifications.Notification).Assembly;

    private static readonly Assembly NotificationsApplication =
        typeof(Marketplace.Modules.Notifications.Application.Notifications.Queries.ListMyNotificationsQuery).Assembly;

    [Fact]
    public void Domains_ne_dependent_pas_de_EFCore()
    {
        AssertNoEf(ReviewsDomain);
        AssertNoEf(SearchDomain);
        AssertNoEf(NotificationsDomain);
    }

    [Fact]
    public void Applications_ne_dependent_ni_de_EFCore_ni_de_leur_Infrastructure()
    {
        AssertNoEfNorInfra(ReviewsApplication, "Marketplace.Modules.Reviews.Infrastructure");
        AssertNoEfNorInfra(SearchApplication, "Marketplace.Modules.Search.Infrastructure");
        AssertNoEfNorInfra(NotificationsApplication, "Marketplace.Modules.Notifications.Infrastructure");
    }

    private static void AssertNoEf(Assembly assembly)
        => Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult().IsSuccessful.Should().BeTrue();

    private static void AssertNoEfNorInfra(Assembly assembly, string infrastructureNamespace)
        => Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", infrastructureNamespace)
            .GetResult().IsSuccessful.Should().BeTrue();
}
