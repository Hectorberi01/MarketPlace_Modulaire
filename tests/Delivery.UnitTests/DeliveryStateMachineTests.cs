using FluentAssertions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Xunit;
using Course = HBA.Deliveries.Domain.Deliveries.Delivery;

namespace Delivery.UnitTests;

/// <summary>
/// La machine à états d'une course. Onze états, des retours en arrière
/// légitimes, et des transitions qui doivent rester impossibles — c'est le genre
/// de code où une erreur produit une course livrée sans avoir été collectée.
/// </summary>
public class DeliveryStateMachineTests
{
    /// <summary>
    /// Ces tests portent sur les TRANSITIONS, pas sur l'argent. Le taux nommé dit
    /// « la rémunération n'est pas le sujet ici » — un « 0m » nu, répété six fois,
    /// laisserait croire à une valeur métier.
    /// Le partage lui-même est éprouvé dans DriverEarningTests.
    /// </summary>
    private const decimal SansPartage = 0m;

    private static readonly Coordinates Pickup = Coordinates.Create(6.3703, 2.3912).Value;
    private static readonly Coordinates Dropoff = Coordinates.Create(6.3556, 2.4180).Value;

    private static Course NewDelivery(ProofOfDeliveryKind proof = ProofOfDeliveryKind.None)
        => Course.Create(
            "ORDER-45821", DeliverySource.HbaExpress, DeliveryType.Express,
            Stop("Boutique ABC", "en face de la pharmacie Sainte-Rita", Pickup),
            Stop("Client", "derrière le marché de Ouando", Dropoff),
            DeliveryPackage.Create("Chaussures", 2.5m).Value,
            proof).Value;

    private static DeliveryStop Stop(string name, string landmark, Coordinates position)
        => DeliveryStop.Create(name, "+229 01 97 24 18 06", "cotonou", "Fidjrossè",
                               landmark, null, position).Value;

    private static Course Accepted(out DriverId driver)
    {
        var delivery = NewDelivery();
        driver = DriverId.New();
        delivery.StartSearching();
        delivery.AssignTo(driver);
        delivery.AcceptByDriver(driver);
        return delivery;
    }

    [Fact]
    public void Une_course_nait_en_attente()
    {
        NewDelivery().Status.Should().Be(DeliveryStatus.Pending);
    }

    [Fact]
    public void On_ne_peut_pas_livrer_un_colis_jamais_collecte()
    {
        var delivery = Accepted(out _);

        delivery.MarkDelivered(null, SansPartage).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Un_refus_renvoie_la_course_en_recherche()
    {
        // Le refus est le fonctionnement NORMAL du dispatch, pas un incident.
        var delivery = NewDelivery();
        var driver = DriverId.New();
        delivery.StartSearching();
        delivery.AssignTo(driver);

        delivery.RejectByDriver(driver, "trop loin").IsSuccess.Should().BeTrue();

        delivery.Status.Should().Be(DeliveryStatus.SearchingDriver);
        delivery.AssignedDriverId.Should().BeNull();
    }

    [Fact]
    public void Un_livreur_ayant_refuse_ne_recoit_plus_la_meme_course()
    {
        var delivery = NewDelivery();
        var driver = DriverId.New();
        delivery.StartSearching();
        delivery.AssignTo(driver);
        delivery.RejectByDriver(driver);

        delivery.AssignTo(driver).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Apres_le_plafond_de_tentatives_la_course_attend_un_humain()
    {
        var delivery = NewDelivery();
        delivery.StartSearching();

        for (var i = 0; i < Course.MaxDispatchAttempts; i++)
        {
            delivery.AssignTo(DriverId.New());
            delivery.RejectByDriver(delivery.Assignments.Last().DriverId);
        }

        // État d'ATTENTE, pas d'échec : la course reste reprenable.
        delivery.Status.Should().Be(DeliveryStatus.NoDriverAvailable);
        delivery.IsTerminal.Should().BeFalse();
        delivery.StartSearching().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void La_collecte_est_toleree_sans_passage_par_arrive_au_point()
    {
        // Un livreur pressé oublie ce bouton ; refuser bloquerait une course
        // physiquement déjà en cours.
        var delivery = Accepted(out _);

        delivery.MarkPickedUp().IsSuccess.Should().BeTrue();
        delivery.Status.Should().Be(DeliveryStatus.PickedUp);
    }

    [Fact]
    public void Une_course_collectee_ne_s_annule_plus()
    {
        var delivery = Accepted(out _);
        delivery.MarkPickedUp();

        var result = delivery.Cancel("le client ne répond pas");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("delivery.already_picked_up");
    }

    [Fact]
    public void La_preuve_n_est_exigee_que_si_elle_a_ete_demandee()
    {
        var sansPreuve = Accepted(out _);
        sansPreuve.MarkPickedUp();
        sansPreuve.MarkInTransit();
        sansPreuve.MarkDelivered(null, SansPartage).IsSuccess.Should().BeTrue();

        var avecPreuve = NewDelivery(ProofOfDeliveryKind.Pin);
        var driver = DriverId.New();
        avecPreuve.StartSearching();
        avecPreuve.AssignTo(driver);
        avecPreuve.AcceptByDriver(driver);
        avecPreuve.MarkPickedUp();
        avecPreuve.MarkInTransit();

        avecPreuve.MarkDelivered(null, SansPartage).IsFailure.Should().BeTrue();

        // ⚠️ CE TEST PASSAIT AVEC « 4271 » ÉCRIT EN DUR, ET C'ÉTAIT LE SYMPTÔME.
        //
        // Aucun code n'était alors émis ni comparé : n'importe quelle chaîne non
        // vide satisfaisait une course exigeant un PIN. Le test vérifiait donc
        // seulement qu'« une chaîne » suffit — c'est-à-dire exactement le défaut,
        // transformé en garantie.
        //
        // Le code est maintenant tiré aléatoirement à la création : il faut le
        // LIRE sur la course. Le comportement de la preuve elle-même est éprouvé
        // dans ProofAndScheduleTests ; ici on ne vérifie que la transition.
        avecPreuve.MarkDelivered(avecPreuve.IssuedPin, SansPartage).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Une_course_livree_est_close()
    {
        var delivery = Accepted(out _);
        delivery.MarkPickedUp();
        delivery.MarkInTransit();
        delivery.MarkDelivered(null, SansPartage);

        delivery.IsTerminal.Should().BeTrue();
        delivery.Cancel("trop tard").IsFailure.Should().BeTrue();
        delivery.MarkInTransit().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void L_historique_des_propositions_est_conserve()
    {
        // C'est le seul moyen de répondre à « pourquoi ma commande a mis
        // quarante minutes à partir ».
        var delivery = NewDelivery();
        delivery.StartSearching();

        var refusant = DriverId.New();
        delivery.AssignTo(refusant);
        delivery.RejectByDriver(refusant, "déjà en course");

        var acceptant = DriverId.New();
        delivery.AssignTo(acceptant);
        delivery.AcceptByDriver(acceptant);

        delivery.Assignments.Should().HaveCount(2);
        delivery.Assignments.First().Outcome.Should().Be(AssignmentOutcome.Rejected);
        delivery.Assignments.First().Reason.Should().Be("déjà en course");
        delivery.Assignments.Last().Outcome.Should().Be(AssignmentOutcome.Accepted);
    }

    [Fact]
    public void Une_course_externe_doit_designer_son_partenaire()
    {
        var sansPartenaire = Course.Create(
            "ORDER-1", DeliverySource.ExternalPartner, DeliveryType.Standard,
            Stop("Boutique", "carrefour Itta", Pickup),
            Stop("Client", "derrière le marché", Dropoff),
            DeliveryPackage.Create("Colis").Value);

        sansPartenaire.IsFailure.Should().BeTrue();
        sansPartenaire.Error.Code.Should().Be("delivery.partner_required");
    }

    [Fact]
    public void Une_course_interne_ne_peut_pas_designer_de_partenaire()
    {
        // L'inverse compte autant : sinon une confusion d'appelant facturerait
        // un tiers pour une de nos propres livraisons.
        var result = Course.Create(
            "ORDER-2", DeliverySource.HbaExpress, DeliveryType.Standard,
            Stop("Boutique", "carrefour Itta", Pickup),
            Stop("Client", "derrière le marché", Dropoff),
            DeliveryPackage.Create("Colis").Value,
            partnerId: Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("delivery.partner_unexpected");
    }

    [Fact]
    public void Collecte_et_remise_au_meme_endroit_sont_refusees()
    {
        var result = Course.Create(
            "ORDER-3", DeliverySource.HbaExpress, DeliveryType.Standard,
            Stop("Boutique", "carrefour Itta", Pickup),
            Stop("Client", "carrefour Itta", Pickup),
            DeliveryPackage.Create("Colis").Value);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("delivery.same_stop");
    }

    [Fact]
    public void Un_point_sans_position_est_refuse()
    {
        var stop = DeliveryStop.Create(
            "Client", "+229 01 97 24 18 06", "cotonou", "Fidjrossè",
            "en face de la pharmacie", null, position: null);

        stop.IsFailure.Should().BeTrue();
        stop.Error.Code.Should().Be("delivery.stop.position_required");
    }

    [Fact]
    public void Un_point_sans_repere_est_refuse()
    {
        // Le repère reste obligatoire MALGRÉ la position : le GPS sert à
        // calculer, le repère sert à trouver.
        var stop = DeliveryStop.Create(
            "Client", "+229 01 97 24 18 06", "cotonou", "Fidjrossè",
            landmark: null, instructions: null, position: Dropoff);

        stop.IsFailure.Should().BeTrue();
        stop.Error.Code.Should().Be("delivery.stop.landmark_required");
    }
}
