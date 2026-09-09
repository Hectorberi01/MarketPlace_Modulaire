using FluentAssertions;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Matching;
using Xunit;

namespace HbaExpress.Dispatch.UnitTests;

/// <summary>À qui la course est-elle proposée — et surtout, à qui elle ne l'est pas.</summary>
public class CourierMatcherTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Les_coursiers_sont_ordonnes_du_plus_proche_au_plus_loin()
    {
        var job = TestJobs.NewJob(pickupAt: TestJobs.Dantokpa);
        var proche = TestJobs.OnlineCourier(TestJobs.Dantokpa, Now);
        var moinsProche = TestJobs.OnlineCourier(TestJobs.Gbedjromede, Now);

        var matched = CourierMatcher.Match(
            job, new[] { moinsProche, proche }, DispatchPolicy.FirstRound, Now);

        matched.Select(m => m.CourierId).Should().ContainInOrder(proche.Id, moinsProche.Id);
    }

    [Fact]
    public void Un_coursier_hors_rayon_n_est_pas_sollicite()
    {
        var job = TestJobs.NewJob(pickupAt: TestJobs.Dantokpa);
        var loin = TestJobs.OnlineCourier(TestJobs.Godomey, Now);

        var matched = CourierMatcher.Match(job, new[] { loin }, DispatchPolicy.FirstRound, Now);

        // Godomey est à environ 8 km de Dantokpa : hors des 3 km de la première vague.
        // Offrir large ferait accepter quelqu'un qui met vingt minutes à arriver — le
        // vendeur attend, l'acheteur attend, et la course coûte plus qu'elle ne rapporte.
        matched.Should().BeEmpty();
    }

    [Fact]
    public void Elargir_le_rayon_rattrape_le_coursier_eloigne()
    {
        var job = TestJobs.NewJob(pickupAt: TestJobs.Dantokpa);
        var loin = TestJobs.OnlineCourier(TestJobs.Godomey, Now);

        var widened = DispatchPolicy.FirstRound.Widen().Widen();

        matchedShouldContain(CourierMatcher.Match(job, new[] { loin }, widened, Now), loin.Id);

        static void matchedShouldContain(IReadOnlyList<MatchedCourier> matched, CourierId expected)
            => matched.Select(m => m.CourierId).Should().Contain(expected);
    }

    [Fact]
    public void Une_position_perimee_ecarte_le_coursier()
    {
        var job = TestJobs.NewJob(pickupAt: TestJobs.Dantokpa);
        var courier = TestJobs.OnlineCourier(TestJobs.Dantokpa, Now.AddMinutes(-10));

        var matched = CourierMatcher.Match(job, new[] { courier }, DispatchPolicy.FirstRound, Now);

        // Il est marqué « en ligne », mais son application est fermée depuis dix minutes.
        // À moto, il est peut-être à dix kilomètres. Lui offrir la course, c'est perdre
        // les vingt secondes du délai pendant que l'acheteur attend.
        matched.Should().BeEmpty();
    }

    [Fact]
    public void Un_coursier_suspendu_ne_recoit_plus_rien_meme_s_il_etait_en_ligne()
    {
        var job = TestJobs.NewJob(pickupAt: TestJobs.Dantokpa);
        var courier = TestJobs.OnlineCourier(TestJobs.Dantokpa, Now);

        courier.ApplyStanding(CourierStanding.Suspended, DateTime.UtcNow);

        // La suspension le sort de la file IMMÉDIATEMENT : ne pas le faire le laisserait
        // recevoir des offres jusqu'à sa prochaine action dans l'application.
        courier.Availability.Should().Be(CourierAvailability.Offline);
        CourierMatcher.Match(job, new[] { courier }, DispatchPolicy.FirstRound, Now).Should().BeEmpty();
    }

    [Fact]
    public void Un_coursier_hors_de_sa_zone_n_est_pas_sollicite_meme_s_il_est_proche()
    {
        var job = TestJobs.NewJob(pickupAt: TestJobs.Dantokpa);
        var courier = TestJobs.OnlineCourier(TestJobs.Dantokpa, Now, commune: "abomey-calavi");

        // LA ZONE PRIME SUR LA DISTANCE. Deux kilomètres à vol d'oiseau peuvent se
        // trouver de l'autre côté de la lagune — donc à trois quarts d'heure de route.
        CourierMatcher.Match(job, new[] { courier }, DispatchPolicy.FirstRound, Now).Should().BeEmpty();
    }

    [Fact]
    public void Un_coursier_deja_sur_une_course_ne_recoit_pas_d_offre()
    {
        var job = TestJobs.NewJob(pickupAt: TestJobs.Dantokpa);
        var courier = TestJobs.OnlineCourier(TestJobs.Dantokpa, Now);
        courier.MarkOnDelivery();

        CourierMatcher.Match(job, new[] { courier }, DispatchPolicy.FirstRound, Now).Should().BeEmpty();
    }

    [Fact]
    public void Les_coursiers_deja_sollicites_sont_exclus_de_la_re_offre()
    {
        var job = TestJobs.NewJob(pickupAt: TestJobs.Dantokpa);
        var deja = TestJobs.OnlineCourier(TestJobs.Dantokpa, Now);
        var nouveau = TestJobs.OnlineCourier(TestJobs.Gbedjromede, Now);

        var matched = CourierMatcher.Match(
            job, new[] { deja, nouveau }, DispatchPolicy.FirstRound.Widen(), Now,
            exclude: new HashSet<CourierId> { deja.Id });

        matched.Select(m => m.CourierId).Should().Equal(nouveau.Id);
    }
}
