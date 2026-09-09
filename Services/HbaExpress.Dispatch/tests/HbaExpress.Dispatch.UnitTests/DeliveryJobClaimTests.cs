using FluentAssertions;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using Xunit;

namespace HbaExpress.Dispatch.UnitTests;

/// <summary>
/// L'INVARIANT LE PLUS COÛTEUX DU SERVICE : une course, un coursier.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// Si deux coursiers gagnent la même course, deux hommes roulent vers la même boutique.
/// L'un des deux a brûlé son carburant pour rien, la plateforme lui doit une
/// compensation qu'aucune règle ne prévoit, et le vendeur ne sait pas à qui remettre le
/// colis. C'est le genre d'erreur qui se règle en argent et en confiance perdue.
///
/// Ces tests couvrent le PREMIER des deux étages du verrou — la règle métier, lisible et
/// testable sans base. Le second étage, le jeton de concurrence en base, ne peut se
/// vérifier qu'en test d'intégration : deux transactions parallèles lisent toutes deux
/// « Offered » et passent toutes deux ce contrôle-ci. Les deux étages sont nécessaires.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class DeliveryJobClaimTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(25);

    [Fact]
    public void Le_premier_qui_accepte_gagne_et_le_second_est_refuse()
    {
        var job = TestJobs.NewJob();
        var first = CourierId.New();
        var second = CourierId.New();

        job.Offer(new[] { first, second }, Now, Window).IsSuccess.Should().BeTrue();

        job.Accept(first, Now.AddSeconds(3)).IsSuccess.Should().BeTrue();

        var late = job.Accept(second, Now.AddSeconds(4));
        late.IsFailure.Should().BeTrue();
        late.Error.Code.Should().Be("job.already_taken");

        job.AssignedCourierId.Should().Be(first);
        job.Status.Should().Be(DeliveryJobStatus.Accepted);
    }

    [Fact]
    public void Le_perdant_est_SUPPLANTE_et_non_marque_comme_refusant()
    {
        var job = TestJobs.NewJob();
        var winner = CourierId.New();
        var loser = CourierId.New();

        job.Offer(new[] { winner, loser }, Now, Window);
        job.Accept(winner, Now.AddSeconds(2));

        // ═══════════════════════════════════════════════════════════════════════
        // LA DISTINCTION N'EST PAS COSMÉTIQUE. Le taux d'acceptation d'un coursier
        // décide des courses qu'il recevra demain. Compter comme « refus » le fait
        // d'avoir été battu d'une seconde punirait la lenteur du réseau et pousserait
        // les coursiers à accepter en aveugle pour protéger leur score.
        // ═══════════════════════════════════════════════════════════════════════
        var loserOffer = job.Offers.Single(o => o.CourierId == loser);
        loserOffer.Status.Should().Be(DispatchOfferStatus.Superseded);
        loserOffer.Status.Should().NotBe(DispatchOfferStatus.Declined);
    }

    [Fact]
    public void Une_offre_expiree_ne_peut_plus_etre_acceptee()
    {
        var job = TestJobs.NewJob();
        var courier = CourierId.New();
        job.Offer(new[] { courier }, Now, Window);

        var tooLate = job.Accept(courier, Now + Window + TimeSpan.FromSeconds(1));

        tooLate.IsFailure.Should().BeTrue();
        tooLate.Error.Code.Should().Be("job.offer_expired");
        job.AssignedCourierId.Should().BeNull();
    }

    [Fact]
    public void Un_coursier_non_sollicite_ne_peut_pas_s_attribuer_une_course()
    {
        var job = TestJobs.NewJob();
        job.Offer(new[] { CourierId.New() }, Now, Window);

        // Le refus est INDISCERNABLE d'une course inexistante. Le commentaire précédent
        // défendait la distinction — « une tentative qui mérite d'être visible » — et
        // c'est vrai du JOURNAL, pas de la réponse : distinguer « pas proposée » de
        // « introuvable » laissait un coursier cartographier la table des courses.
        var intruder = job.Accept(CourierId.New(), Now.AddSeconds(1));

        intruder.IsFailure.Should().BeTrue();
        // « INTROUVABLE », et non « pas proposée » : le refus ne doit pas confirmer que
        // la course existe. Voir DeliveryJob.Accept.
        intruder.Error.Code.Should().Be("job.not_found");
    }

    [Fact]
    public void Une_re_offre_supplante_la_vague_precedente_sans_la_refuser()
    {
        var job = TestJobs.NewJob();
        var firstRound = CourierId.New();
        var secondRound = CourierId.New();

        job.Offer(new[] { firstRound }, Now, Window);
        job.Offer(new[] { secondRound }, Now + Window, Window);

        job.CurrentRound.Should().Be(2);
        job.Offers.Single(o => o.CourierId == firstRound).Status.Should().Be(DispatchOfferStatus.Superseded);

        // Le coursier de la première vague ne peut plus gagner : son offre est morte.
        job.Accept(firstRound, Now + Window + TimeSpan.FromSeconds(1)).IsFailure.Should().BeTrue();
        job.Accept(secondRound, Now + Window + TimeSpan.FromSeconds(1)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Un_refus_laisse_la_course_offerte_aux_autres()
    {
        var job = TestJobs.NewJob();
        var refuser = CourierId.New();
        var taker = CourierId.New();

        job.Offer(new[] { refuser, taker }, Now, Window);
        job.Decline(refuser, Now.AddSeconds(2)).IsSuccess.Should().BeTrue();

        job.Status.Should().Be(DeliveryJobStatus.Offered);
        job.Accept(taker, Now.AddSeconds(5)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Une_course_expiree_sort_du_dispatch_sans_echouer()
    {
        var job = TestJobs.NewJob();
        job.Offer(new[] { CourierId.New() }, Now, Window);

        job.Expire(Now.AddMinutes(5)).IsSuccess.Should().BeTrue();

        // EXPIRÉE N'EST PAS ÉCHOUÉE : c'est l'entrée dans le repli en cascade (CDC §5.7)
        // — prestataire externe, puis transporteur classique. Confondre les deux ferait
        // rembourser un acheteur qu'on peut encore livrer.
        job.Status.Should().Be(DeliveryJobStatus.Expired);
        job.Status.Should().NotBe(DeliveryJobStatus.Failed);
        job.Offers.Should().OnlyContain(o => o.Status == DispatchOfferStatus.Expired);
    }
}
