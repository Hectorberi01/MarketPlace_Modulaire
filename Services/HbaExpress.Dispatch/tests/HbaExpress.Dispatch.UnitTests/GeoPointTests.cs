using FluentAssertions;
using HbaExpress.Dispatch.Domain.Couriers;
using Xunit;

namespace HbaExpress.Dispatch.UnitTests;

public class GeoPointTests
{
    [Fact]
    public void La_distance_entre_deux_points_de_Cotonou_est_plausible()
    {
        var distance = TestJobs.Dantokpa.DistanceKmTo(TestJobs.Gbedjromede);

        // Dantokpa → Gbedjromede : environ 1,5 km à vol d'oiseau.
        distance.Should().BeInRange(1.2, 1.8);
    }

    [Fact]
    public void La_distance_est_symetrique_et_nulle_sur_place()
    {
        TestJobs.Dantokpa.DistanceKmTo(TestJobs.Dantokpa).Should().BeApproximately(0d, 0.0001);

        TestJobs.Dantokpa.DistanceKmTo(TestJobs.Godomey)
            .Should().BeApproximately(TestJobs.Godomey.DistanceKmTo(TestJobs.Dantokpa), 0.0001);
    }

    [Fact]
    public void Le_point_zero_zero_est_refuse()
    {
        // (0, 0) est au large du golfe de Guinée : c'est ce qu'envoie un client dont le
        // GPS n'a pas de fix. L'accepter placerait des coursiers en pleine mer, et le
        // rayon de dispatch ne trouverait jamais personne sans qu'on comprenne pourquoi.
        var refused = GeoPoint.Create(0d, 0d);

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("courier.geo.null_island");
    }
}
