using FluentAssertions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Domain.Pricing;
using HBA.Deliveries.Infrastructure.Persistence;
using Xunit;

namespace Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA CONFIGURATION TARIFAIRE SEMÉE AU DÉMARRAGE.
///
/// POURQUOI DES DONNÉES MÉRITENT DES TESTS
///
/// Un contour de zone, ce sont quatre couples de nombres saisis à la main. Une
/// latitude et une longitude interverties, une décimale déplacée, et le rectangle
/// atterrit dans le golfe de Guinée. Rien ne proteste : la zone existe, elle est
/// active, elle ne contient simplement jamais personne.
///
/// La conséquence n'est pas une panne mais une SOUS-FACTURATION SILENCIEUSE. Le
/// devis retombe sur le facteur routier par défaut (1,4 au lieu de 2,19 au centre
/// de Cotonou), soit un tiers de la distance facturée en moins, sur chaque course,
/// indéfiniment. C'est le genre d'erreur qu'on découvre en comptabilité.
///
/// Ces tests exercent exactement les données qui partent en production.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public class DefaultPricingConfigurationTests
{
    private static readonly IReadOnlyList<DeliveryZone> Zones = DeliveriesPricingSeeder.BuildZones().ToList();
    private static readonly IReadOnlyList<PricingRule> Rules = DeliveriesPricingSeeder.BuildRules().ToList();

    /// <summary>Repères réels, et la zone qui doit les couvrir.</summary>
    public static TheoryData<string, double, double, string> Reperes => new()
    {
        { "Carrefour Saint-Michel",   6.3703, 2.3912, "Cotonou — centre" },
        { "Marché Dantokpa",          6.3697, 2.4300, "Cotonou — centre" },
        { "Ganhi",                    6.3654, 2.4183, "Cotonou — centre" },
        { "Akpakpa",                  6.3550, 2.4450, "Cotonou — Akpakpa" },
        { "Aéroport de Cadjèhoun",    6.3572, 2.3844, "Cotonou — centre" },
        { "Fidjrossè plage",          6.3600, 2.3600, "Cotonou — Fidjrossè / Cadjèhoun" },
        { "Godomey carrefour",        6.3770, 2.3300, "Godomey" },
        { "Abomey-Calavi centre",     6.4485, 2.3556, "Abomey-Calavi — centre" },
    };

    [Theory]
    [MemberData(nameof(Reperes))]
    public void Chaque_repere_tombe_dans_la_zone_attendue(string repere, double lat, double lon, string zoneAttendue)
    {
        var position = Coordinates.Create(lat, lon).Value;

        var zone = PricingCalculator.ResolveZone(Zones, position);

        zone.Should().NotBeNull($"« {repere} » doit être couvert par une zone tarifaire");
        zone!.Name.Should().Be(zoneAttendue);
    }

    [Fact]
    public void L_exemple_du_cahier_reste_vrai_avec_la_configuration_reelle()
    {
        // Le test de PricingCalculatorTests utilise une zone et une grille
        // FABRIQUÉES pour l'occasion. Celui-ci refait le même calcul avec ce qui
        // part réellement en production. Les deux peuvent diverger — c'est
        // précisément l'écart qu'on veut voir.
        var request = new QuoteRequest(
            Coordinates.Create(6.3703, 2.3912).Value,
            Coordinates.Create(6.3556, 2.4180).Value,
            Weight.FromKilograms(2.5m).Value,
            PackageSize.Small,
            DeliveryType.Express,
            VehicleType.Motorcycle);

        var result = PricingCalculator.Calculate(request, Zones, Rules, DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        result.Value.DistanceKm.Should().Be(7.4);
        result.Value.Total.Should().Be(2100m);
    }

    [Fact]
    public void Chaque_vehicule_operable_a_une_grille()
    {
        // ⚠️ LE PIÈGE QUI A DÉJÀ FRAPPÉ DEUX FOIS DANS CE MODULE.
        //
        // Tricycle avait été ajouté à VehicleType sans être ajouté au dispatch
        // (corrigé), puis il a failli l'être sans grille tarifaire. Un véhicule
        // sans grille est intarifable : « pricing.no_rule » à chaque devis, et
        // l'on en conclut qu'il n'y a pas de demande.
        //
        // Ce test ferme la boucle : ajouter un véhicule à l'énumération sans
        // décider de son prix casse la construction, au lieu de créer un trou.
        VehicleType[] operables =
        [
            VehicleType.Motorcycle,
            VehicleType.Tricycle,
            VehicleType.Car,
            VehicleType.Van
        ];

        foreach (var vehicule in operables)
        {
            var request = new QuoteRequest(
                Coordinates.Create(6.3703, 2.3912).Value,
                Coordinates.Create(6.3556, 2.4180).Value,
                Weight.FromKilograms(2.5m).Value,
                PackageSize.Small,
                DeliveryType.Standard,
                vehicule);

            PricingCalculator.Calculate(request, Zones, Rules, DateTime.UtcNow)
                .IsSuccess.Should().BeTrue($"« {vehicule} » doit disposer d'une grille tarifaire");
        }

        // L'inverse est VOLONTAIRE, et documenté comme tel : inventer un tarif
        // vélo reviendrait à décider d'un prix à la place de l'exploitation.
        var velo = new QuoteRequest(
            Coordinates.Create(6.3703, 2.3912).Value,
            Coordinates.Create(6.3556, 2.4180).Value,
            Weight.FromKilograms(2.5m).Value,
            PackageSize.Small,
            DeliveryType.Standard,
            VehicleType.Bicycle);

        var refus = PricingCalculator.Calculate(velo, Zones, Rules, DateTime.UtcNow);
        refus.IsFailure.Should().BeTrue();
        refus.Error.Code.Should().Be("pricing.no_rule");
    }

    [Fact]
    public void Les_priorites_de_zone_sont_toutes_distinctes()
    {
        // À priorité égale, deux zones qui se recouvrent sont départagées par un
        // GUID tiré au hasard à la création. Deux installations de la même version
        // factureraient alors différemment la même course, sans explication
        // possible. Le recouvrement est admis ; l'ambiguïté ne l'est pas.
        Zones.Select(z => z.Priority).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Aucun_facteur_routier_n_est_laisse_a_la_valeur_par_defaut()
    {
        // Une zone au facteur par défaut est le symptôme d'un oubli : si l'on
        // prend la peine de tracer un contour, c'est qu'on a une raison de penser
        // que le trajet y est particulier.
        Zones.Should().OnlyContain(z => Math.Abs(z.RoadFactor - DeliveryZone.DefaultRoadFactor) > 0.001);
    }

    [Fact]
    public void Les_facteurs_routiers_restent_plausibles()
    {
        // Bornes larges à dessein : on cherche la faute de frappe (2,19 saisi
        // 21,9), pas à valider le calibrage — lequel ne peut venir que de courses
        // réellement livrées.
        Zones.Should().OnlyContain(z => z.RoadFactor >= 1.3 && z.RoadFactor <= 3.0);
    }

    [Fact]
    public void Les_noms_sont_uniques_car_c_est_la_cle_d_idempotence()
    {
        // L'amorçage reconnaît un élément déjà présent PAR SON NOM. Deux zones
        // homonymes rendraient le semis non idempotent : la seconde ne serait
        // jamais créée, et personne ne saurait laquelle manque.
        Zones.Select(z => z.Name).Should().OnlyHaveUniqueItems();
        Rules.Select(r => r.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Chaque_grille_a_un_plancher_superieur_a_son_prix_de_base()
    {
        // Sinon le plancher ne sert à rien : une course de 200 m serait facturée
        // au prix de base, qui ne couvre pas le déplacement du livreur.
        Rules.Should().OnlyContain(r => r.MinimumPrice > r.BasePrice);
    }

    [Fact]
    public void Les_grilles_sont_ordonnees_du_plus_leger_au_plus_lourd()
    {
        // Une camionnette moins chère qu'une moto passerait la compilation, les
        // tests unitaires, la revue — et se verrait le jour où un client
        // choisirait la camionnette pour économiser.
        decimal PrixPour(VehicleType v) => Rules.Single(r => r.VehicleType == v).PricePerKm;

        PrixPour(VehicleType.Motorcycle)
            .Should().BeLessThan(PrixPour(VehicleType.Tricycle));

        PrixPour(VehicleType.Tricycle)
            .Should().BeLessThan(PrixPour(VehicleType.Car));

        PrixPour(VehicleType.Car)
            .Should().BeLessThan(PrixPour(VehicleType.Van));
    }

    [Fact]
    public void Les_paliers_de_poids_couvrent_la_capacite_annoncee_du_vehicule()
    {
        // Un colis hors palier ne paie AUCUNE surcharge — il n'est pas refusé.
        // Un tricycle chargé à 140 kg au tarif d'un colis de 30 kg, c'est une
        // course à perte, et rien ne la signale.
        (VehicleType Vehicle, decimal CapaciteKg)[] capacites =
        [
            (VehicleType.Motorcycle, DeliveryPackage.MotorcycleMaxWeightKg),
            (VehicleType.Tricycle, 150m),
        ];

        foreach (var (vehicle, capacite) in capacites)
        {
            var grille = Rules.Single(r => r.VehicleType == vehicle);
            var auPlafond = Weight.FromKilograms(capacite - 0.5m).Value;

            grille.WeightTiers
                .Any(t => t.Covers(auPlafond.Kilograms))
                .Should().BeTrue(
                    $"la grille « {grille.Name} » doit couvrir un colis de {capacite - 0.5m} kg, "
                    + "sinon il voyage sans surcharge de poids");
        }
    }
}
