using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>Frontières des modules read-model / scoring : Recommendations, Analytics, Fraud.</summary>
public class ReadModelScoringBoundaryTests
{
    private static readonly Assembly RecommendationsDomain =
        typeof(Marketplace.Modules.Recommendations.Domain.Recommendations.Recommendation).Assembly;

    private static readonly Assembly RecommendationsApplication =
        typeof(Marketplace.Modules.Recommendations.Application.Recommendations.UpsertRecommendationCommand).Assembly;

    private static readonly Assembly AnalyticsDomain =
        typeof(Marketplace.Modules.Analytics.Domain.Metrics.MetricSnapshot).Assembly;

    private static readonly Assembly AnalyticsApplication =
        typeof(Marketplace.Modules.Analytics.Application.Metrics.GetReportQuery).Assembly;

    private static readonly Assembly FraudDomain =
        typeof(Marketplace.Modules.Fraud.Domain.Risk.RiskAssessment).Assembly;

    private static readonly Assembly FraudApplication =
        typeof(Marketplace.Modules.Fraud.Application.Risk.AssessRiskCommand).Assembly;

    [Fact]
    public void Domains_ne_dependent_pas_de_EFCore()
    {
        AssertNoEf(RecommendationsDomain);
        AssertNoEf(AnalyticsDomain);
        AssertNoEf(FraudDomain);
    }

    [Fact]
    public void Applications_ne_dependent_ni_de_EFCore_ni_de_leur_Infrastructure()
    {
        AssertCleanApplication(RecommendationsApplication, "Marketplace.Modules.Recommendations.Infrastructure");
        AssertCleanApplication(AnalyticsApplication, "Marketplace.Modules.Analytics.Infrastructure");
        AssertCleanApplication(FraudApplication, "Marketplace.Modules.Fraud.Infrastructure");
    }

    private static void AssertNoEf(Assembly assembly)
        => Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult().IsSuccessful.Should().BeTrue();

    private static void AssertCleanApplication(Assembly assembly, string infrastructureNamespace)
        => Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", infrastructureNamespace)
            .GetResult().IsSuccessful.Should().BeTrue();
}
