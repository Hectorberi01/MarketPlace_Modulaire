using FluentAssertions;
using Marketplace.Modules.Recommendations.Domain.Recommendations;
using Xunit;

namespace Recommendations.UnitTests;

public class RecommendationTests
{
    [Fact]
    public void Create_dedupe_et_ignore_les_guids_vides()
    {
        var p1 = Guid.NewGuid();
        var reco = Recommendation.Create(RecommendationType.Similar, Guid.NewGuid(), null, new[] { p1, p1, Guid.Empty }, 0.9);

        reco.RecommendedProductIds.Should().ContainSingle().Which.Should().Be(p1);
        reco.Type.Should().Be(RecommendationType.Similar);
    }

    [Fact]
    public void Refresh_remplace_la_liste_et_le_score()
    {
        var reco = Recommendation.Create(RecommendationType.Personalized, null, Guid.NewGuid(), new[] { Guid.NewGuid() }, 0.2);
        var newList = new[] { Guid.NewGuid(), Guid.NewGuid() };

        reco.Refresh(newList, 0.75);

        reco.RecommendedProductIds.Should().BeEquivalentTo(newList);
        reco.Score.Should().Be(0.75);
    }
}
