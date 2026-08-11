using FluentAssertions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Marketplace.BuildingBlocks.Domain.Results;
using Xunit;
using Course = HBA.Deliveries.Domain.Deliveries.Delivery;

namespace Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES TROIS VALEURS QUI NE PROUVAIENT RIEN.
///
/// <c>DeliveryType.Scheduled</c>, <c>ProofOfDeliveryKind.Photo</c> et
/// <c>.Signature</c> existaient dans les énumérations sans aucune machinerie
/// derrière. Une course « programmée » partait immédiatement ; une course exigeant
/// une photo se fermait sur le mot « ok ». Le PIN, lui, n'était ni émis ni comparé.
///
/// Ces tests existent pour que le prochain qui ajoute une valeur à l'une de ces
/// énumérations sans écrire ce qu'il y a derrière casse la construction, au lieu
/// de livrer une garantie de façade.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public class ProofAndScheduleTests
{
    private const decimal SansPartage = 0m;

    private static readonly Coordinates Pickup = Coordinates.Create(6.3703, 2.3912).Value;
    private static readonly Coordinates Dropoff = Coordinates.Create(6.3556, 2.4180).Value;

    private static readonly DateTime Maintenant = new(2026, 8, 11, 10, 0, 0, DateTimeKind.Utc);

    private static Result<Course> Create(
        DeliveryType type = DeliveryType.Standard,
        ProofOfDeliveryKind proof = ProofOfDeliveryKind.None,
        DateTime? scheduledFor = null)
        => Course.Create(
            "ORDER-45821", DeliverySource.HbaExpress, type,
            Stop("Boutique", "en face de la pharmacie Sainte-Rita", Pickup),
            Stop("Client", "derrière le marché de Ouando", Dropoff),
            DeliveryPackage.Create("Colis", 2.5m).Value,
            proof, partnerId: null, scheduledForUtc: scheduledFor, nowUtc: Maintenant);

    private static DeliveryStop Stop(string name, string landmark, Coordinates position)
        => DeliveryStop.Create(name, "+229 01 97 24 18 06", "cotonou", "Fidjrossè",
                               landmark, null, position).Value;

    /// <summary>Une course acceptée, collectée, en transit — prête à être remise.</summary>
    private static Course EnTransit(ProofOfDeliveryKind proof)
    {
        var delivery = Create(proof: proof).Value;
        var driver = DriverId.New();
        delivery.StartSearching();
        delivery.AssignTo(driver);
        delivery.AcceptByDriver(driver);
        delivery.MarkPickedUp();
        delivery.MarkInTransit();
        return delivery;
    }

    // ── Le créneau ──────────────────────────────────────────────────────────

    [Fact]
    public void Une_course_programmee_sans_date_est_refusee()
    {
        var result = Create(DeliveryType.Scheduled);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("delivery.schedule_required");
    }

    [Fact]
    public void Une_date_sur_une_course_non_programmee_est_refusee()
    {
        // L'inverse compte autant : une date ignorée en silence sur une course
        // express, et personne ne comprend pourquoi le créneau n'est pas respecté.
        var result = Create(DeliveryType.Express, scheduledFor: Maintenant.AddHours(3));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("delivery.schedule_unexpected");
    }

    [Fact]
    public void Un_creneau_trop_proche_est_refuse()
    {
        // Programmer « dans dix minutes » n'est pas une programmation : c'est une
        // course express avec une promesse qu'on ne tiendra pas.
        var result = Create(DeliveryType.Scheduled, scheduledFor: Maintenant.AddMinutes(10));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("delivery.schedule_too_soon");
    }

    [Fact]
    public void Un_creneau_au_dela_de_l_horizon_est_refuse()
    {
        var result = Create(
            DeliveryType.Scheduled,
            scheduledFor: Maintenant.AddDays(Course.MaxScheduleHorizonDays + 1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("delivery.schedule_too_far");
    }

    [Fact]
    public void Une_course_programmee_ne_part_pas_au_dispatch_avant_l_heure()
    {
        // ⚠️ LE TEST QUI DONNE ENFIN UN SENS AU TYPE « Scheduled ».
        //
        // Sans cette garde, la création appelait StartSearching immédiatement : le
        // client choisissait un créneau pour 18 h et voyait son livreur à 10 h.
        var delivery = Create(DeliveryType.Scheduled, scheduledFor: Maintenant.AddHours(8)).Value;

        delivery.Status.Should().Be(DeliveryStatus.Pending);

        var tropTot = delivery.StartSearching(Maintenant);
        tropTot.IsFailure.Should().BeTrue();
        tropTot.Error.Code.Should().Be("delivery.scheduled_not_due");
    }

    [Fact]
    public void La_fenetre_s_ouvre_avant_l_heure_promise_pas_a_l_heure()
    {
        // Chercher un livreur à l'heure exacte garantirait d'être en retard : il
        // faut encore le trouver, qu'il rejoigne la boutique, et qu'il roule.
        var heure = Maintenant.AddHours(8);
        var delivery = Create(DeliveryType.Scheduled, scheduledFor: heure).Value;

        delivery.DispatchWindowOpensAtUtc.Should().Be(heure - Course.ScheduledDispatchLeadTime);

        // Une minute avant l'ouverture : toujours refusé.
        delivery.StartSearching(delivery.DispatchWindowOpensAtUtc!.Value.AddMinutes(-1))
            .IsFailure.Should().BeTrue();

        // À l'ouverture : la recherche démarre.
        delivery.StartSearching(delivery.DispatchWindowOpensAtUtc!.Value)
            .IsSuccess.Should().BeTrue();

        delivery.Status.Should().Be(DeliveryStatus.SearchingDriver);
    }

    // ── La preuve ───────────────────────────────────────────────────────────

    [Fact]
    public void Un_code_est_emis_a_la_creation_et_seulement_pour_le_genre_Pin()
    {
        Create(proof: ProofOfDeliveryKind.Pin).Value.IssuedPin
            .Should().NotBeNullOrWhiteSpace().And.HaveLength(ProofOfDelivery.PinLength);

        Create(proof: ProofOfDeliveryKind.Photo).Value.IssuedPin.Should().BeNull();
        Create(proof: ProofOfDeliveryKind.None).Value.IssuedPin.Should().BeNull();
    }

    [Fact]
    public void Deux_courses_n_ont_pas_le_meme_code()
    {
        // Un PIN prévisible ne vaut pas mieux que pas de PIN. La menace n'est pas
        // la force brute sur quatre chiffres — c'est la prévisibilité.
        var codes = Enumerable.Range(0, 50)
            .Select(_ => Create(proof: ProofOfDeliveryKind.Pin).Value.IssuedPin)
            .ToList();

        codes.Distinct().Should().HaveCountGreaterThan(40);
    }

    [Fact]
    public void Un_mauvais_code_ne_ferme_pas_la_course()
    {
        // ⚠️ CE TEST ÉCHOUAIT AVANT CORRECTION : n'importe quelle chaîne passait.
        var delivery = EnTransit(ProofOfDeliveryKind.Pin);

        var faux = delivery.IssuedPin == "0000" ? "1111" : "0000";
        var result = delivery.MarkDelivered(faux, SansPartage);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("delivery.proof.pin_mismatch");
        delivery.Status.Should().NotBe(DeliveryStatus.Delivered);
    }

    [Fact]
    public void Le_bon_code_ferme_la_course_et_la_preuve_est_conservee()
    {
        var delivery = EnTransit(ProofOfDeliveryKind.Pin);

        delivery.MarkDelivered(delivery.IssuedPin, SansPartage).IsSuccess.Should().BeTrue();

        delivery.Proof.Should().NotBeNull();
        delivery.Proof!.Kind.Should().Be(ProofOfDeliveryKind.Pin);
        delivery.Proof.CapturedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("photo prise")]
    [InlineData("x")]
    [InlineData("livré")]
    public void Une_photo_ne_peut_pas_etre_du_texte_libre(string bidon)
    {
        // ⚠️ LE CŒUR DU DÉFAUT : le livreur tapait « ok » et une course exigeant
        // une photo se fermait, en affichant au client qu'elle était prouvée.
        var delivery = EnTransit(ProofOfDeliveryKind.Photo);

        var result = delivery.MarkDelivered(bidon, SansPartage);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("delivery.proof.file_required");
        delivery.Status.Should().NotBe(DeliveryStatus.Delivered);
    }

    [Fact]
    public void Une_photo_televersee_est_acceptee()
    {
        var delivery = EnTransit(ProofOfDeliveryKind.Photo);

        delivery.MarkDelivered("https://cdn.hba-express.org/proofs/2026/08/abc123.jpg", SansPartage)
            .IsSuccess.Should().BeTrue();

        delivery.Proof!.Kind.Should().Be(ProofOfDeliveryKind.Photo);
    }

    [Fact]
    public void Une_signature_suit_la_meme_regle_que_la_photo()
    {
        var delivery = EnTransit(ProofOfDeliveryKind.Signature);

        delivery.MarkDelivered("signature", SansPartage).IsFailure.Should().BeTrue();
        delivery.MarkDelivered("proofs/2026/08/sign-abc123.png", SansPartage).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Une_course_sans_preuve_exigee_se_ferme_sans_rien_fournir()
    {
        var delivery = EnTransit(ProofOfDeliveryKind.None);

        delivery.MarkDelivered(null, SansPartage).IsSuccess.Should().BeTrue();
        delivery.Proof.Should().BeNull();
    }

    // ── Le plafond de tentatives ────────────────────────────────────────────

    [Fact]
    public void Le_code_ne_se_devine_pas_par_force_brute()
    {
        // ⚠️ CETTE FAILLE A EXISTÉ, ET C'EST MOI QUI L'AI ÉCRITE.
        //
        // J'avais commenté que « le livreur n'a qu'une poignée de tentatives » —
        // rien ne comptait. Dix mille combinaisons, une route appelable en boucle,
        // et un limiteur de débit qui partitionne par compte livreur : sa propre
        // file, 300 par minute. Une demi-heure pour clore une livraison jamais
        // faite.
        var delivery = EnTransit(ProofOfDeliveryKind.Pin);
        var faux = delivery.IssuedPin == "0000" ? "1111" : "0000";

        for (var i = 0; i < Course.MaxFailedProofAttempts; i++)
        {
            delivery.MarkDelivered(faux, SansPartage).IsFailure.Should().BeTrue();
        }

        delivery.IsProofLocked.Should().BeTrue();

        // Et surtout : le BON code ne passe plus non plus. Un verrou qui céderait
        // au bon code ne protégerait de rien — c'est précisément le bon code que
        // l'attaquant finit par trouver.
        var apresVerrou = delivery.MarkDelivered(delivery.IssuedPin, SansPartage);

        apresVerrou.IsFailure.Should().BeTrue();
        apresVerrou.Error.Code.Should().Be("delivery.proof.locked");
        delivery.Status.Should().NotBe(DeliveryStatus.Delivered);
    }

    [Fact]
    public void Une_preuve_absente_ne_consomme_pas_de_tentative()
    {
        // Sinon un livreur verrouillerait sa propre course en appelant cinq fois
        // une route sans corps — un déni de service à un appel près. Seule une
        // MAUVAISE RÉPONSE compte.
        var delivery = EnTransit(ProofOfDeliveryKind.Pin);

        for (var i = 0; i < Course.MaxFailedProofAttempts + 3; i++)
        {
            delivery.MarkDelivered(null, SansPartage).IsFailure.Should().BeTrue();
        }

        delivery.FailedProofAttempts.Should().Be(0);
        delivery.IsProofLocked.Should().BeFalse();
        delivery.MarkDelivered(delivery.IssuedPin, SansPartage).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Les_tentatives_restantes_suffisent_a_un_client_qui_dicte_son_code()
    {
        // Quatre erreurs de saisie puis la bonne : le livreur légitime passe.
        var delivery = EnTransit(ProofOfDeliveryKind.Pin);
        var faux = delivery.IssuedPin == "0000" ? "1111" : "0000";

        for (var i = 0; i < Course.MaxFailedProofAttempts - 1; i++)
        {
            delivery.MarkDelivered(faux, SansPartage);
        }

        delivery.MarkDelivered(delivery.IssuedPin, SansPartage).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Chaque_genre_de_preuve_a_un_mode_de_capture()
    {
        // ⚠️ LE TEST QUI FERME LA BOUCLE POUR L'AVENIR.
        //
        // Photo et Signature sont restées décoratives parce que rien ne reliait
        // l'énumération à une implémentation. Ajouter une valeur sans écrire sa
        // capture doit désormais échouer ici, et non en production.
        foreach (var kind in Enum.GetValues<ProofOfDeliveryKind>().Where(k => k is not ProofOfDeliveryKind.None))
        {
            var result = ProofOfDelivery.Capture(kind, value: null, expectedPin: null, DateTime.UtcNow);

            // La valeur est absente, donc le refus est attendu — mais il doit
            // porter « preuve manquante », PAS « genre non pris en charge ».
            result.Error.Code.Should().NotBe(
                "delivery.proof.kind_unsupported",
                $"« {kind} » doit avoir un mode de capture défini dans ProofOfDelivery");
        }
    }
}
