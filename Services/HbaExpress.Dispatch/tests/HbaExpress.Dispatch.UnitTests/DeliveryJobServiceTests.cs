using FluentAssertions;
using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Application.Jobs;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using HbaExpress.Dispatch.Domain.Matching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HbaExpress.Dispatch.UnitTests;

/// <summary>
/// L'enchaînement complet : la marketplace annonce une commande prête, une course naît,
/// et les coursiers proches reçoivent une offre.
/// </summary>
public class DeliveryJobServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Une_commande_prete_cree_une_course_et_offre_aux_coursiers_proches()
    {
        var proche = TestJobs.OnlineCourier(TestJobs.Dantokpa, Now);
        var loin = TestJobs.OnlineCourier(TestJobs.Godomey, Now);
        var (service, jobs, notifier) = Harness(proche, loin);

        var created = await service.CreateFromBriefAsync(Brief());

        created.IsSuccess.Should().BeTrue();
        var job = jobs.Single();
        job.Status.Should().Be(DeliveryJobStatus.Offered);

        // Seul le coursier dans le rayon est sollicité — et il reçoit bien un message.
        job.Offers.Select(o => o.CourierId).Should().Equal(proche.Id);
        notifier.Offered.Should().Equal(proche.Id);
        notifier.Offered.Should().NotContain(loin.Id);
    }

    [Fact]
    public async Task Une_annonce_REJOUEE_ne_cree_pas_une_seconde_course()
    {
        var (service, jobs, _) = Harness(TestJobs.OnlineCourier(TestJobs.Dantokpa, Now));
        var brief = Brief();

        var first = await service.CreateFromBriefAsync(brief);
        var replay = await service.CreateFromBriefAsync(brief);

        // Deux vendeurs qui terminent à la même seconde peuvent tous deux conclure
        // « commande prête ». Deux courses, ce serait deux coursiers pour une livraison.
        replay.Value.Should().Be(first.Value);
        jobs.Should().HaveCount(1);
    }

    [Fact]
    public async Task Sans_aucun_coursier_eligible_la_course_existe_quand_meme()
    {
        var (service, jobs, notifier) = Harness(TestJobs.OnlineCourier(TestJobs.Godomey, Now));

        var created = await service.CreateFromBriefAsync(Brief());

        // ═══════════════════════════════════════════════════════════════════════
        // LA COURSE SURVIT À L'ABSENCE DE COURSIER, et c'est le point.
        //
        // Échouer ici perdrait la commande : la marketplace a déjà considéré son
        // annonce comme délivrée, et rien ne la republierait. La course reste donc
        // « Created », et le balayage de redispatch l'élargira — ou la fera basculer
        // vers le repli en cascade.
        // ═══════════════════════════════════════════════════════════════════════
        created.IsSuccess.Should().BeTrue();
        jobs.Single().Status.Should().Be(DeliveryJobStatus.Created);
        notifier.Offered.Should().BeEmpty();
    }

    [Fact]
    public async Task Une_course_sans_point_de_repere_est_refusee_et_signalee()
    {
        var (service, jobs, _) = Harness(TestJobs.OnlineCourier(TestJobs.Dantokpa, Now));
        var brief = Brief() with
        {
            Pickups = new[] { Brief().Pickups[0] with { Landmark = null } }
        };

        var created = await service.CreateFromBriefAsync(brief);

        // Sans repère, le coursier ne trouve pas la boutique. Refuser la course coûte
        // moins cher que de la faire accepter puis abandonner sur place.
        created.IsFailure.Should().BeTrue();
        created.Error.Code.Should().Be("job.pickup_landmark_required");
        jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Accepter_rend_le_coursier_indisponible_et_retire_l_offre_aux_autres()
    {
        var winner = TestJobs.OnlineCourier(TestJobs.Dantokpa, Now);
        var loser = TestJobs.OnlineCourier(TestJobs.Gbedjromede, Now);
        var (service, jobs, notifier) = Harness(winner, loser);

        await service.CreateFromBriefAsync(Brief());
        var job = jobs.Single();

        (await service.AcceptAsync(job.Id, winner.Id)).IsSuccess.Should().BeTrue();

        job.AssignedCourierId.Should().Be(winner.Id);
        winner.Availability.Should().Be(CourierAvailability.OnDelivery,
            "un coursier en course ne doit plus recevoir d'offres");

        // Le perdant voit l'offre disparaître de son écran plutôt que de l'accepter
        // pour rien.
        notifier.Withdrawn.Should().Contain(loser.Id);
    }

    [Fact]
    public async Task Un_echec_de_notification_ne_fait_PAS_echouer_le_dispatch()
    {
        var courier = TestJobs.OnlineCourier(TestJobs.Dantokpa, Now);
        var (service, jobs, _) = Harness(new ExplodingNotifier(), courier);

        var created = await service.CreateFromBriefAsync(Brief());

        // Le hub temps réel tombera — c'est un réseau mobile béninois. L'offre reste
        // valide en base, et le coursier la verra à sa prochaine ouverture. La perdre
        // parce qu'un message n'est pas passé serait pire que le silence.
        created.IsSuccess.Should().BeTrue();
        jobs.Single().Status.Should().Be(DeliveryJobStatus.Offered);
        jobs.Single().Offers.Should().ContainSingle();
    }

    private static DeliveryBrief Brief() => new(
        Guid.NewGuid(), 1_500m, "XOF",
        new[]
        {
            new PickupBrief(
                Guid.NewGuid(), Guid.NewGuid(), "Boutique Wax", "+2290197123456",
                "cotonou", "Gbedjromede", "En face de la pharmacie", null,
                TestJobs.Dantokpa.Latitude, TestJobs.Dantokpa.Longitude,
                new[] { new ParcelBrief("ROBE-WAX-M", 2) })
        },
        new DropoffBrief(
            Guid.NewGuid(), "Kossi", "+2290197000000",
            "cotonou", "Fidjrosse", "Après le carrefour", null,
            TestJobs.Gbedjromede.Latitude, TestJobs.Gbedjromede.Longitude));

    private static (DeliveryJobService, List<DeliveryJob>, RecordingNotifier) Harness(params Courier[] couriers)
    {
        var notifier = new RecordingNotifier();
        var (service, jobs, _) = Harness(notifier, couriers);
        return (service, jobs, notifier);
    }

    private static (DeliveryJobService, List<DeliveryJob>, IOfferNotifier) Harness(
        IOfferNotifier notifier, params Courier[] couriers)
    {
        var jobs = new FakeJobRepository();
        var service = new DeliveryJobService(
            jobs, new FakeCourierRepository(couriers), notifier, new NoOpUnitOfWork(),
            NullLogger<DeliveryJobService>.Instance,
            new FixedClock(Now));

        return (service, jobs.Jobs, notifier);
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTime nowUtc) => _now = new DateTimeOffset(nowUtc);
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class NoOpUnitOfWork : IDispatchUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeJobRepository : IDeliveryJobRepository
    {
        public List<DeliveryJob> Jobs { get; } = new();

        public Task AddAsync(DeliveryJob job, CancellationToken cancellationToken = default)
        {
            Jobs.Add(job);
            return Task.CompletedTask;
        }

        public Task<DeliveryJob?> GetByIdAsync(DeliveryJobId id, CancellationToken cancellationToken = default)
            => Task.FromResult(Jobs.FirstOrDefault(j => j.Id == id));

        public Task<DeliveryJob?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult(Jobs.FirstOrDefault(j => j.OrderId == orderId));

        public Task<IReadOnlyList<DeliveryJob>> ListAwaitingRedispatchAsync(
            DateTime nowUtc, int take, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeliveryJob>>(
                Jobs.Where(j => j.Status == DeliveryJobStatus.Offered).ToList());

        public Task<IReadOnlyList<DeliveryJob>> ListOfferedToAsync(
            CourierId courierId, DateTime nowUtc, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeliveryJob>>(
                Jobs.Where(j => j.Offers.Any(o => o.CourierId == courierId && o.IsLive(nowUtc))).ToList());

        public Task<DeliveryJob?> GetActiveForCourierAsync(
            CourierId courierId, CancellationToken cancellationToken = default)
            => Task.FromResult(Jobs.FirstOrDefault(j => j.AssignedCourierId == courierId));
    }

    private sealed class FakeCourierRepository : ICourierRepository
    {
        private readonly List<Courier> _couriers;

        public FakeCourierRepository(IEnumerable<Courier> couriers) => _couriers = couriers.ToList();

        public Task AddAsync(Courier courier, CancellationToken cancellationToken = default)
        {
            _couriers.Add(courier);
            return Task.CompletedTask;
        }

        public Task<Courier?> GetByIdAsync(CourierId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_couriers.FirstOrDefault(c => c.Id == id));

        public Task<IReadOnlyList<Courier>> ListCandidatesAsync(
            string communeCode, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Courier>>(
                _couriers.Where(c => c.Serves(communeCode)).ToList());
    }

    private sealed class RecordingNotifier : IOfferNotifier
    {
        public List<CourierId> Offered { get; } = new();
        public List<CourierId> Withdrawn { get; } = new();

        public Task NotifyOfferAsync(CourierId courierId, DeliveryJob job, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
        {
            Offered.Add(courierId);
            return Task.CompletedTask;
        }

        public Task NotifyOfferWithdrawnAsync(CourierId courierId, DeliveryJobId jobId, CancellationToken cancellationToken = default)
        {
            Withdrawn.Add(courierId);
            return Task.CompletedTask;
        }
    }

    private sealed class ExplodingNotifier : IOfferNotifier
    {
        public Task NotifyOfferAsync(CourierId courierId, DeliveryJob job, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("hub injoignable");

        public Task NotifyOfferWithdrawnAsync(CourierId courierId, DeliveryJobId jobId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("hub injoignable");
    }
}
