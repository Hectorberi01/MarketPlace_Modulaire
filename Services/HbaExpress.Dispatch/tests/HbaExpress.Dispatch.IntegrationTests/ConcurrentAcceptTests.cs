using FluentAssertions;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using HbaExpress.Dispatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace HbaExpress.Dispatch.IntegrationTests;

/// <summary>
/// LE TEST QUI JUSTIFIE TOUT LE RESTE : deux coursiers acceptent la même course, dans
/// la même seconde, depuis deux connexions. UN SEUL doit gagner.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE TEST NE PEUT PAS ÊTRE UNITAIRE.
///
/// Le contrôle métier de DeliveryJob.Accept lit un état chargé en mémoire. En test
/// unitaire, un seul objet existe : le second appel voit forcément le premier. La
/// situation dangereuse — deux transactions qui lisent toutes deux « Offered » avant
/// que l'une n'écrive — ne peut se reproduire qu'avec une VRAIE base.
///
/// Sans ce test, le verrou de concurrence pourrait être inerte sans que rien ne le
/// signale : les tests unitaires passeraient, la revue de code aussi, et deux hommes
/// rouleraient vers la même boutique en production.
///
/// Docker requis (Testcontainers).
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ConcurrentAcceptTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("dispatch")
        .Build();

    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // EnsureCreated plutôt que Migrate : ce test porte sur la SÉMANTIQUE du verrou,
        // pas sur le contenu des migrations. Construire le schéma depuis le modèle valide
        // au passage que la configuration EF tient debout.
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private DispatchDbContext NewContext()
        => new(new DbContextOptionsBuilder<DispatchDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    [Fact]
    public async Task Deux_coursiers_qui_acceptent_en_parallele_UN_SEUL_gagne()
    {
        var first = CourierId.New();
        var second = CourierId.New();
        var jobId = await SeedOfferedJobAsync(first, second);

        // Deux contextes distincts : deux connexions, deux transactions. C'est la
        // reproduction fidèle de deux téléphones qui appuient au même instant.
        await using var contextA = NewContext();
        await using var contextB = NewContext();

        var jobA = await LoadAsync(contextA, jobId);
        var jobB = await LoadAsync(contextB, jobId);

        // LES DEUX LISENT « Offered » ET PASSENT LE CONTRÔLE MÉTIER. C'est précisément
        // l'instant où le domaine seul ne suffit plus.
        jobA.Accept(first, Now.AddSeconds(1)).IsSuccess.Should().BeTrue();
        jobB.Accept(second, Now.AddSeconds(1)).IsSuccess.Should().BeTrue();

        await contextA.SaveChangesAsync();

        var loser = async () => await contextB.SaveChangesAsync();
        await loser.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "xmin doit faire échouer l'UPDATE du second : sa ligne a changé sous lui");

        await using var verification = NewContext();
        var persisted = await LoadAsync(verification, jobId);

        persisted.Status.Should().Be(DeliveryJobStatus.Accepted);
        persisted.AssignedCourierId.Should().Be(first, "le premier à avoir écrit garde la course");

        var winning = persisted.Offers.Single(o => o.CourierId == first);
        var losing = persisted.Offers.Single(o => o.CourierId == second);
        winning.Status.Should().Be(DispatchOfferStatus.Accepted);
        losing.Status.Should().Be(DispatchOfferStatus.Superseded);
    }

    [Fact]
    public async Task Une_commande_ne_peut_pas_avoir_DEUX_courses()
    {
        var orderId = Guid.NewGuid();

        await using var context = NewContext();
        await context.DeliveryJobs.AddAsync(NewJob(orderId));
        await context.SaveChangesAsync();

        // L'événement « commande prête » peut arriver deux fois : deux vendeurs qui
        // terminent à la même seconde travaillent dans deux transactions distinctes, et
        // chacune peut conclure que tout est prêt. Un contrôle applicatif seul les
        // laisserait toutes deux passer — deux courses, deux coursiers, une commande.
        await using var duplicate = NewContext();
        await duplicate.DeliveryJobs.AddAsync(NewJob(orderId));

        var second = async () => await duplicate.SaveChangesAsync();
        await second.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Le_parcours_complet_survit_a_un_aller_retour_en_base()
    {
        var courier = CourierId.New();
        var jobId = await SeedOfferedJobAsync(courier);

        await using (var accepting = NewContext())
        {
            var job = await LoadAsync(accepting, jobId);
            job.Accept(courier, Now.AddSeconds(1));
            job.StartPickup();
            foreach (var pickup in job.PickupsInSequence)
            {
                job.MarkPickedUp(pickup.Id, Now.AddMinutes(10));
            }

            job.StartTransit().IsSuccess.Should().BeTrue();
            await accepting.SaveChangesAsync();
        }

        await using var reloaded = NewContext();
        var persisted = await LoadAsync(reloaded, jobId);

        // Les états des ENFANTS survivent aussi : sans cela, un coursier reconnecté après
        // une coupure réseau reverrait des ramassages qu'il a déjà faits.
        persisted.Status.Should().Be(DeliveryJobStatus.InTransit);
        persisted.Pickups.Should().OnlyContain(p => p.IsPickedUp);
    }

    private static async Task<DeliveryJob> LoadAsync(DispatchDbContext context, DeliveryJobId id)
        => await context.DeliveryJobs
            .Include(j => j.Offers)
            .Include(j => j.Pickups)
            .ThenInclude(p => p.Parcels)
            .FirstAsync(j => j.Id == id);

    private async Task<DeliveryJobId> SeedOfferedJobAsync(params CourierId[] couriers)
    {
        var job = NewJob(Guid.NewGuid());
        job.Offer(couriers, Now, TimeSpan.FromSeconds(30)).IsSuccess.Should().BeTrue();

        await using var context = NewContext();
        await context.DeliveryJobs.AddAsync(job);
        await context.SaveChangesAsync();
        return job.Id;
    }

    private static DeliveryJob NewJob(Guid orderId)
        => DeliveryJob.Create(
            orderId, 1_500m, "XOF",
            new[]
            {
                new PickupDraft(
                    Guid.NewGuid(), Guid.NewGuid(), "Boutique Wax", "+2290197123456",
                    "cotonou", "Gbedjromede", "En face de la pharmacie", null,
                    GeoPoint.Create(6.3703, 2.4183).Value,
                    new[] { ("ROBE-WAX-M", 2) })
            },
            new DropoffDraft(
                Guid.NewGuid(), "Kossi", "+2290197000000",
                "cotonou", "Fidjrosse", "Après le carrefour", null,
                GeoPoint.Create(6.3835, 2.4155).Value)).Value;
}
