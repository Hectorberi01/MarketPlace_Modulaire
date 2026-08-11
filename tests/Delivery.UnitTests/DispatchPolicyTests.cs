using FluentAssertions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Dispatch;
using HBA.Deliveries.Domain.Drivers;
using Xunit;
using Course = HBA.Deliveries.Domain.Deliveries.Delivery;

namespace Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CHOIX DU LIVREUR.
///
/// C'est la règle la plus visible du produit : elle décide qui gagne sa journée
/// et combien de temps le client attend. Elle est écrite comme une fonction pure
/// précisément pour pouvoir être éprouvée ici, sans base ni cache — et pour
/// pouvoir être réglée plus tard en ayant la preuve que le réglage fait ce qu'on
/// croit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public class DispatchPolicyTests
{
    /// <summary>Point de collecte de référence : carrefour Saint-Michel, Cotonou.</summary>
    private static readonly Coordinates Pickup = Coordinates.Create(6.3703, 2.3912).Value;

    /// <summary>Un degré de latitude ≈ 111,19 km — sert à placer un livreur à N km pile.</summary>
    private const double KmPerDegreeLatitude = 111.19;

    [Fact]
    public void A_experience_egale_le_plus_proche_gagne()
    {
        var delivery = NewDelivery();

        var proche = Candidate(Motorcyclist(), km: 0.5);
        var loin = Candidate(Motorcyclist(), km: 3.0);

        var ranked = DispatchPolicy.Rank(delivery, new[] { loin, proche });

        ranked.Should().HaveCount(2);
        ranked.First().DriverId.Should().Be(proche.Driver.Id);
        ranked.First().DistanceKm.Should().BeApproximately(0.5, 0.05);
    }

    [Fact]
    public void L_experience_departage_a_distance_egale()
    {
        var delivery = NewDelivery();

        var novice = Candidate(Motorcyclist(), km: 1.5);
        var chevronne = Candidate(Motorcyclist(completedDeliveries: 100), km: 1.5);

        var ranked = DispatchPolicy.Rank(delivery, new[] { novice, chevronne });

        ranked.First().DriverId.Should().Be(chevronne.Driver.Id);
    }

    [Fact]
    public void L_experience_ne_renverse_pas_un_ecart_de_distance_important()
    {
        // C'est la promesse tenue au client : ce qu'il ressent, c'est l'attente.
        // Un vétéran à 3 km ne doit pas passer devant un débutant à 800 m.
        var delivery = NewDelivery();

        var debutantProche = Candidate(Motorcyclist(), km: 0.8);
        var veteranLoin = Candidate(Motorcyclist(completedDeliveries: 500), km: 3.0);

        var ranked = DispatchPolicy.Rank(delivery, new[] { veteranLoin, debutantProche });

        ranked.First().DriverId.Should().Be(debutantProche.Driver.Id);
    }

    [Fact]
    public void Un_livreur_indisponible_est_ecarte_pas_mal_note()
    {
        // La nuance compte : un livreur écarté ne peut pas remonter au premier rang
        // le jour où l'on ajoute un critère favorable. Un livreur mal noté, si.
        var delivery = NewDelivery();

        var horsLigne = Motorcyclist();
        horsLigne.GoOffline();

        var enPause = Motorcyclist();
        enPause.TakeBreak();

        var enCourse = Motorcyclist();
        enCourse.MarkBusy();

        var nonVerifie = Driver.Register(Guid.NewGuid(), "Non vérifié", "+229 01 97 00 00 09", VehicleType.Motorcycle).Value;

        var suspendu = Motorcyclist();
        suspendu.Suspend("pièces expirées");

        var candidates = new[] { horsLigne, enPause, enCourse, nonVerifie, suspendu }
            .Select(d => Candidate(d, km: 0.2));

        DispatchPolicy.Rank(delivery, candidates).Should().BeEmpty();
    }

    [Fact]
    public void Le_vehicule_est_eliminatoire()
    {
        // Quarante kilos : la moto ne peut pas, la camionnette oui. Aucun degré de
        // proximité ne change cela — et c'est bien pour ça que le critère est
        // éliminatoire et non pondéré.
        var delivery = NewDelivery(weightKg: 40m);

        var motoTresProche = Candidate(Motorcyclist(), km: 0.1);
        var vanLoin = Candidate(DriverWith(VehicleType.Van), km: 4.0);

        var ranked = DispatchPolicy.Rank(delivery, new[] { motoTresProche, vanLoin });

        ranked.Should().ContainSingle();
        ranked.Single().DriverId.Should().Be(vanLoin.Driver.Id);
    }

    [Fact]
    public void Un_colis_de_poids_inconnu_est_presume_transportable_a_moto()
    {
        // Personne ne pèse un repas. Refuser faute de poids déclaré immobiliserait
        // la quasi-totalité de la flotte pour la quasi-totalité des courses.
        var delivery = NewDelivery(weightKg: null);

        DispatchPolicy.Rank(delivery, new[] { Candidate(Motorcyclist(), km: 1.0) })
            .Should().ContainSingle();
    }

    [Fact]
    public void Le_velo_et_la_marche_exigent_un_poids_connu()
    {
        // Ici la présomption s'inverse : sur un vélo, un colis dont le poids est
        // inconnu est un risque physique, pas un aléa commercial.
        var sansPoids = NewDelivery(weightKg: null);

        DispatchPolicy.Rank(sansPoids, new[]
        {
            Candidate(DriverWith(VehicleType.Bicycle), km: 0.5),
            Candidate(DriverWith(VehicleType.OnFoot), km: 0.3),
        }).Should().BeEmpty();

        var leger = NewDelivery(weightKg: 3m);

        DispatchPolicy.Rank(leger, new[]
        {
            Candidate(DriverWith(VehicleType.Bicycle), km: 0.5),
            Candidate(DriverWith(VehicleType.OnFoot), km: 0.3),
        }).Should().HaveCount(2);
    }

    [Fact]
    public void Le_tricycle_prend_les_charges_que_la_moto_refuse()
    {
        // ⚠️ CE TEST NAÎT D'UN BOGUE TROUVÉ EN L'ÉCRIVANT.
        //
        // Tricycle avait été ajouté à VehicleType sans être ajouté au switch de
        // CanCarry : il tombait dans « _ => false ». Un tricycle enregistré,
        // vérifié, en ligne, garé devant la boutique, n'aurait JAMAIS reçu de
        // course — sans erreur, sans journal, sans rien. Le livreur en aurait
        // conclu qu'il n'y a pas de demande.
        //
        // C'est précisément le genre de trou qu'aucune compilation ne montre et
        // qu'aucune revue de code ne remarque.
        var tricycle = DriverWith(VehicleType.Tricycle);

        DispatchPolicy.CanCarry(tricycle, DeliveryPackage.Create("Sacs de riz", 80m).Value)
            .Should().BeTrue();

        // Il reste borné : au-delà, c'est une camionnette.
        DispatchPolicy.CanCarry(tricycle, DeliveryPackage.Create("Mobilier", 200m).Value)
            .Should().BeFalse();

        // Et il est effectivement CLASSÉ, pas seulement jugé apte.
        DispatchPolicy.Rank(NewDelivery(weightKg: 80m), new[] { Candidate(tricycle, km: 1.0) })
            .Should().ContainSingle();
    }

    [Fact]
    public void Au_dela_du_rayon_le_livreur_n_est_pas_retenu()
    {
        var delivery = NewDelivery();

        var dansLeRayon = Candidate(Motorcyclist(), km: 4.5);
        var horsRayon = Candidate(Motorcyclist(), km: 6.0);

        var ranked = DispatchPolicy.Rank(delivery, new[] { dansLeRayon, horsRayon });

        ranked.Should().ContainSingle();
        ranked.Single().DriverId.Should().Be(dansLeRayon.Driver.Id);

        // Le second tour élargit, et le rattrape.
        DispatchPolicy.Rank(delivery, new[] { dansLeRayon, horsRayon }, DispatchPolicy.ExtendedRadiusKm)
            .Should().HaveCount(2);
    }

    [Fact]
    public void Un_livreur_ayant_deja_refuse_n_est_plus_propose()
    {
        var delivery = NewDelivery();
        var refusant = Motorcyclist();

        delivery.StartSearching();
        delivery.AssignTo(refusant.Id);
        delivery.RejectByDriver(refusant.Id, "déjà engagé");

        DispatchPolicy.Rank(delivery, new[] { Candidate(refusant, km: 0.1) })
            .Should().BeEmpty();
    }

    [Fact]
    public void Une_expiration_ne_vaut_pas_un_refus()
    {
        // Un livreur qui n'a pas répondu à temps — tunnel, écran verrouillé — n'a
        // pas dit non. L'exclure définitivement viderait la flotte à chaque zone
        // de mauvaise couverture.
        var delivery = NewDelivery();
        var silencieux = Motorcyclist();

        delivery.StartSearching();
        delivery.AssignTo(silencieux.Id);
        delivery.RejectByDriver(silencieux.Id, expired: true);

        DispatchPolicy.Rank(delivery, new[] { Candidate(silencieux, km: 0.1) })
            .Should().ContainSingle();
    }

    [Fact]
    public void La_distance_se_mesure_au_point_de_collecte()
    {
        // Erreur classique : classer sur la distance au CLIENT. Elle est la même
        // pour tout le monde et ne dit rien du délai de départ.
        var delivery = NewDelivery();

        var presDeLaCollecte = Candidate(Motorcyclist(), km: 1.0);
        var presDeLaRemise = new DriverCandidate(Motorcyclist(), delivery.Dropoff.Position);

        var ranked = DispatchPolicy.Rank(delivery, new[] { presDeLaRemise, presDeLaCollecte });

        ranked.First().DriverId.Should().Be(presDeLaCollecte.Driver.Id);
    }

    [Fact]
    public void Le_classement_est_stable_entre_deux_appels()
    {
        // Deux livreurs à égalité parfaite existent : même position relevée, même
        // compteur. Sans départage déterministe, la course changerait de
        // destinataire d'un appel à l'autre et deux téléphones sonneraient.
        var delivery = NewDelivery();
        var candidates = new[]
        {
            Candidate(Motorcyclist(), km: 2.0),
            Candidate(Motorcyclist(), km: 2.0),
            Candidate(Motorcyclist(), km: 2.0),
        };

        var premier = DispatchPolicy.Rank(delivery, candidates).Select(s => s.DriverId).ToList();
        var second = DispatchPolicy.Rank(delivery, candidates.Reverse().ToList()).Select(s => s.DriverId).ToList();

        second.Should().Equal(premier);
    }

    [Fact]
    public void La_note_reste_dans_l_intervalle_attendu()
    {
        // Garde-fou : une note hors [0,1] signalerait que proximité et expérience
        // ne sont plus sur la même échelle, et le classement n'aurait plus de sens.
        var delivery = NewDelivery();

        var ranked = DispatchPolicy.Rank(delivery, new[]
        {
            Candidate(Motorcyclist(), km: 0.0),
            Candidate(Motorcyclist(completedDeliveries: 100), km: 0.0),
            Candidate(Motorcyclist(), km: DispatchPolicy.DefaultRadiusKm),
        });

        ranked.Should().OnlyContain(s => s.Score >= 0.0 && s.Score <= 1.0);
    }

    // ── Aides ───────────────────────────────────────────────────────────────

    private static Course NewDelivery(decimal? weightKg = 2.5m)
        => Course.Create(
            "ORDER-45821", DeliverySource.HbaExpress, DeliveryType.Express,
            Stop("Boutique ABC", "en face de la pharmacie Sainte-Rita", Pickup),
            Stop("Client", "derrière le marché de Ouando", Coordinates.Create(6.3556, 2.4180).Value),
            DeliveryPackage.Create("Colis", weightKg).Value).Value;

    private static DeliveryStop Stop(string name, string landmark, Coordinates position)
        => DeliveryStop.Create(name, "+229 01 97 24 18 06", "cotonou", "Fidjrossè",
                               landmark, null, position).Value;

    private static Driver Motorcyclist(int completedDeliveries = 0)
        => DriverWith(VehicleType.Motorcycle, completedDeliveries);

    /// <summary>Un livreur actif, en ligne, avec le compteur de courses demandé.</summary>
    private static Driver DriverWith(VehicleType vehicle, int completedDeliveries = 0)
    {
        var driver = Driver.Register(
            Guid.NewGuid(), $"Livreur {vehicle}", "+229 01 97 24 18 06", vehicle).Value;

        driver.Verify();
        driver.GoOnline();

        // CompletedDeliveries n'a pas de mutateur public — c'est voulu : il ne se
        // gagne que course après course. On les rejoue donc réellement.
        for (var i = 0; i < completedDeliveries; i++)
        {
            driver.MarkBusy();
            driver.CompleteMission();
        }

        return driver;
    }

    /// <summary>Place un livreur à exactement <paramref name="km"/> au nord du point de collecte.</summary>
    private static DriverCandidate Candidate(Driver driver, double km)
    {
        var position = Coordinates.Create(
            Pickup.Latitude + (km / KmPerDegreeLatitude), Pickup.Longitude).Value;

        return new DriverCandidate(driver, position);
    }
}
