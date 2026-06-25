using FluentAssertions;
using Marketplace.Modules.Analytics.Domain.Metrics;
using Xunit;

namespace Analytics.UnitTests;

public class AnalyticsTests
{
    [Fact]
    public void Accrue_accumule_commandes_gmv_et_articles()
    {
        var snapshot = MetricSnapshot.Create(MetricScope.Platform, null, "2026-06-24", MetricGranularity.Day, "XOF");

        snapshot.Accrue(10_000m, 3);
        snapshot.Accrue(5_000m, 2);

        snapshot.OrdersCount.Should().Be(2);
        snapshot.GmvAmount.Should().Be(15_000m);
        snapshot.ItemsSold.Should().Be(5);
    }

    [Fact]
    public void Create_seller_porte_un_proprietaire()
    {
        var sellerId = Guid.NewGuid();
        var snapshot = MetricSnapshot.Create(MetricScope.Seller, sellerId, "2026-06", MetricGranularity.Month, "XOF");

        snapshot.Scope.Should().Be(MetricScope.Seller);
        snapshot.OwnerId.Should().Be(sellerId);
        snapshot.OrdersCount.Should().Be(0);
    }

    [Fact]
    public void Mark_trace_la_commande_traitee()
        => ProcessedOrder.Mark(Guid.NewGuid()).ProcessedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
}
