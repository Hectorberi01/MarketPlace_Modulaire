using System.Text.Json;
using FluentAssertions;
using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Application.Jobs;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using HbaExpress.Dispatch.Domain.Outbound;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HbaExpress.Dispatch.UnitTests;

/// <summary>
/// LES DEUX CULS-DE-SAC D'UNE COURSE, ET IL FAUT LES DEUX POUR LES VOIR.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// 1. UN VENDEUR DÉFAILLANT FIGEAIT TOUTE LA COURSE. Un point de ramassage en incident
///    restait à IsPickedUp == false, et StartTransit exigeait que TOUS les points soient
///    ramassés : le coursier ne pouvait plus partir, alors qu'il avait déjà dans son sac
///    les parts des autres vendeurs. Sa seule sortie était de déclarer l'échec.
///
/// 2. ET L'ÉCHEC NE MENAIT NULLE PART. Rien n'était annoncé à la marketplace :
///    l'expédition y restait « en préparation » pour toujours. Le premier défaut poussait
///    donc dans le second, et l'enchaînement des deux perdait la commande sans une seule
///    erreur nulle part.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class JobFailureAndPickupIncidentTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    [Fact]
    public void Un_vendeur_defaillant_ne_bloque_plus_la_livraison_des_autres()
    {
        var (job, _) = PickingUpJob(pickupCount: 2);
        var points = job.PickupsInSequence;

        job.MarkPickedUp(points[0].Id, Now.AddMinutes(10)).IsSuccess.Should().BeTrue();
        job.ReportPickupIncident(points[1].Id, "Boutique fermée").IsSuccess.Should().BeTrue();

        // C'est TOUT le correctif : la course part avec ce qu'elle a. Avant, elle restait
        // bloquée en ramassage et la part déjà collectée n'arrivait jamais chez
        // l'acheteur.
        job.StartTransit().IsSuccess.Should().BeTrue();
        job.Status.Should().Be(DeliveryJobStatus.InTransit);
    }

    [Fact]
    public void Un_incident_ne_fait_PAS_croire_que_le_colis_est_dans_le_sac()
    {
        var (job, _) = PickingUpJob(pickupCount: 1);
        var point = job.PickupsInSequence[0];

        job.ReportPickupIncident(point.Id, "Vendeur absent");

        // La distinction est la raison d'être de IsResolved. Débloquer le départ en
        // marquant le point « ramassé » aurait été plus court d'une ligne et aurait menti
        // sur ce que le coursier transporte — un incident de ramassage serait devenu un
        // litige de livraison chez l'acheteur.
        point.IsPickedUp.Should().BeFalse();
        point.IsResolved.Should().BeTrue();
        point.Incident.Should().Be("Vendeur absent");
    }

    [Fact]
    public void Un_ramassage_ni_fait_ni_signale_retient_toujours_la_course()
    {
        var (job, _) = PickingUpJob(pickupCount: 2);
        var points = job.PickupsInSequence;

        job.MarkPickedUp(points[0].Id, Now.AddMinutes(10));

        // L'assouplissement ne va pas jusqu'à laisser partir sur un point qu'on n'a même
        // pas visité : un ramassage oublié reste un ramassage à faire.
        var tooEarly = job.StartTransit();
        tooEarly.IsFailure.Should().BeTrue();
        tooEarly.Error.Code.Should().Be("job.pickups_incomplete");
    }

    [Fact]
    public void Une_course_dont_TOUS_les_ramassages_ont_echoue_ne_part_pas_a_vide()
    {
        var (job, _) = PickingUpJob(pickupCount: 2);

        foreach (var point in job.PickupsInSequence)
        {
            job.ReportPickupIncident(point.Id, "Boutique fermée");
        }

        // Tous les points sont « traités », donc la première condition passe. Partir
        // quand même enverrait un coursier les mains vides chez l'acheteur et
        // produirait une livraison à zéro colis.
        var empty = job.StartTransit();
        empty.IsFailure.Should().BeTrue();
        empty.Error.Code.Should().Be("job.nothing_to_deliver");
        job.Status.Should().Be(DeliveryJobStatus.PickingUp);
    }

    [Fact]
    public async Task Un_echec_de_course_met_une_annonce_en_file_pour_la_marketplace()
    {
        var (job, courier) = PickingUpJob(pickupCount: 1);
        var (progress, outbound, _) = Harness(job, courier);

        var failed = await progress.FailAsync(job.Id, courier.Id, "  Acheteur injoignable  ");

        failed.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(DeliveryJobStatus.Failed);

        // AVANT : rien du tout ici. La course se fermait en base et la marketplace ne
        // l'apprenait jamais.
        var message = outbound.Messages.Should().ContainSingle().Subject;
        message.Type.Should().Be(OutboundMessage.CourierDeliveryFailedType);

        var payload = JsonSerializer.Deserialize<CourierDeliveryFailedPayload>(
            message.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        // De quoi retrouver l'expédition figée, et de quoi dire pourquoi.
        payload.OrderId.Should().Be(job.OrderId);
        payload.DeliveryJobId.Should().Be(job.Id.Value);
        payload.CourierId.Should().Be(courier.Id.Value);
        payload.Reason.Should().Be("Acheteur injoignable");
    }

    [Fact]
    public async Task L_annonce_d_echec_est_en_file_AVANT_l_enregistrement()
    {
        var (job, courier) = PickingUpJob(pickupCount: 1);
        var (progress, _, unitOfWork) = Harness(job, courier);

        await progress.FailAsync(job.Id, courier.Id, "Colis refusé");

        // ═══════════════════════════════════════════════════════════════════════
        // LE POINT DE TOUT LE MÉCANISME : LES DEUX ÉCRITURES PARTENT ENSEMBLE.
        //
        // Le statut de la course et l'annonce qui la débloque côté marketplace sont
        // enregistrés par le MÊME SaveChanges. S'ils étaient séparés, une coupure entre
        // les deux laisserait une course close dont personne n'apprend l'échec — et il
        // ne resterait rien à réessayer.
        // ═══════════════════════════════════════════════════════════════════════
        unitOfWork.OutboundCountAtSave.Should().Be(1);
    }

    [Fact]
    public async Task Un_motif_vide_ne_traverse_pas_la_file()
    {
        var (job, courier) = PickingUpJob(pickupCount: 1);
        var (progress, outbound, _) = Harness(job, courier);

        await progress.FailAsync(job.Id, courier.Id, "   ");

        // La raison est relue SUR LA COURSE, où Fail l'a normalisée — pas reprise du
        // corps de la requête. Une chaîne vide arrivant côté marketplace ne dirait rien
        // à personne.
        var payload = JsonSerializer.Deserialize<CourierDeliveryFailedPayload>(
            outbound.Messages.Single().Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        payload.Reason.Should().NotBeNullOrWhiteSpace();
        payload.Reason.Should().Be(job.FailureReason);
    }

    /// <summary>Une course acceptée par un coursier et entrée en phase de ramassage.</summary>
    private static (DeliveryJob Job, Courier Courier) PickingUpJob(int pickupCount)
    {
        var job = TestJobs.NewJob(pickupCount);
        var courier = TestJobs.OnlineCourier(TestJobs.Dantokpa, Now);

        job.Offer(new[] { courier.Id }, Now, Window).IsSuccess.Should().BeTrue();
        job.Accept(courier.Id, Now.AddSeconds(1)).IsSuccess.Should().BeTrue();
        job.StartPickup().IsSuccess.Should().BeTrue();

        return (job, courier);
    }

    private static (JobProgressService, FakeOutboundRepository, RecordingUnitOfWork) Harness(
        DeliveryJob job, Courier courier)
    {
        var outbound = new FakeOutboundRepository();
        var unitOfWork = new RecordingUnitOfWork(outbound);

        var progress = new JobProgressService(
            new FakeJobRepository(job),
            new FakeCourierRepository(courier),
            outbound,
            new CourierPayRule(0.8m),
            unitOfWork,
            NullLogger<JobProgressService>.Instance,
            new FixedClock(Now.AddMinutes(30)));

        return (progress, outbound, unitOfWork);
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTime nowUtc) => _now = new DateTimeOffset(nowUtc);

        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>
    /// Enregistre ce que la file contenait À L'INSTANT DU SaveChanges. C'est la seule
    /// façon, sans base, de vérifier que le message n'a pas été écrit APRÈS.
    /// </summary>
    private sealed class RecordingUnitOfWork : IDispatchUnitOfWork
    {
        private readonly FakeOutboundRepository _outbound;

        public RecordingUnitOfWork(FakeOutboundRepository outbound) => _outbound = outbound;

        public int? OutboundCountAtSave { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            OutboundCountAtSave = _outbound.Messages.Count;
            return Task.FromResult(0);
        }

        public Task<T> InTransactionAsync<T>(
            Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
            => work(cancellationToken);
    }

    private sealed class FakeOutboundRepository : IOutboundMessageRepository
    {
        public List<OutboundMessage> Messages { get; } = new();

        public Task AddAsync(OutboundMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboundMessage>> ListPendingAsync(
            DateTime nowUtc, int take, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OutboundMessage>>(
                Messages.Where(m => m.IsPending).Take(take).ToList());

        public Task<int> CountDeadLetteredAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Messages.Count(m => m.DeadLetteredAtUtc is not null));
    }

    private sealed class FakeJobRepository : IDeliveryJobRepository
    {
        private readonly List<DeliveryJob> _jobs;

        public FakeJobRepository(params DeliveryJob[] jobs) => _jobs = jobs.ToList();

        public Task AddAsync(DeliveryJob job, CancellationToken cancellationToken = default)
        {
            _jobs.Add(job);
            return Task.CompletedTask;
        }

        public Task<DeliveryJob?> GetByIdAsync(DeliveryJobId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_jobs.FirstOrDefault(j => j.Id == id));

        public Task<DeliveryJob?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult(_jobs.FirstOrDefault(j => j.OrderId == orderId));

        public Task<IReadOnlyList<DeliveryJob>> ListAwaitingRedispatchAsync(
            DateTime nowUtc, int take, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeliveryJob>>(
                _jobs.Where(j => j.Status == DeliveryJobStatus.Offered).ToList());

        public Task<IReadOnlyList<DeliveryJob>> ListOfferedToAsync(
            CourierId courierId, DateTime nowUtc, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeliveryJob>>(
                _jobs.Where(j => j.Offers.Any(o => o.CourierId == courierId && o.IsLive(nowUtc))).ToList());

        public Task<DeliveryJob?> GetActiveForCourierAsync(
            CourierId courierId, CancellationToken cancellationToken = default)
            => Task.FromResult(_jobs.FirstOrDefault(j => j.AssignedCourierId == courierId));
    }

    private sealed class FakeCourierRepository : ICourierRepository
    {
        private readonly List<Courier> _couriers;

        public FakeCourierRepository(params Courier[] couriers) => _couriers = couriers.ToList();

        public Task AddAsync(Courier courier, CancellationToken cancellationToken = default)
        {
            _couriers.Add(courier);
            return Task.CompletedTask;
        }

        public Task<Courier?> GetByIdAsync(CourierId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_couriers.FirstOrDefault(c => c.Id == id));

        public Task<Courier?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(_couriers.FirstOrDefault(c => c.UserId == userId));

        public Task<IReadOnlyList<Courier>> ListCandidatesAsync(
            string communeCode, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Courier>>(
                _couriers.Where(c => c.Serves(communeCode)).ToList());
    }
}
