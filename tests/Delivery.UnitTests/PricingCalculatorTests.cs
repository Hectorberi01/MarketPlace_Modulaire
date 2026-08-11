using FluentAssertions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Domain.Pricing;
using Xunit;

namespace Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CALCUL TARIFAIRE, ÉPROUVÉ SUR LE CODE RÉEL.
///
/// POURQUOI CE FICHIER EXISTE AVANT TOUT AUTRE
///
/// L'exemple du cahier d'architecture — 7,4 km, 2 100 FCFA — avait été vérifié
/// en RÉIMPLÉMENTANT la formule dans un autre langage. Cela prouvait que le
/// raisonnement tenait ; cela ne prouvait rien sur ce code-ci. Deux
/// implémentations d'une même idée peuvent diverger sur un arrondi, un ordre
/// d'opérations, une conversion decimal/double — et c'est précisément là que
/// naissent les écarts de facturation.
///
/// Ces tests exercent PricingCalculator lui-même.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public class PricingCalculatorTests
{
    /// <summary>Les deux points de l'exemple du cahier : 3,38 km à vol d'oiseau.</summary>
    private static readonly Coordinates CahierPickup = Coordinates.Create(6.3703, 2.3912).Value;
    private static readonly Coordinates CahierDropoff = Coordinates.Create(6.3556, 2.4180).Value;

    /// <summary>Zone couvrant largement Cotonou, avec le facteur routier de l'exemple.</summary>
    private static DeliveryZone CotonouZone(double roadFactor = 2.19, decimal baseFee = 0m, decimal extraFee = 0m)
    {
        var boundary = GeoPolygon.Create(new[]
        {
            new GeoPoint(6.30, 2.30),
            new GeoPoint(6.30, 2.50),
            new GeoPoint(6.45, 2.50),
            new GeoPoint(6.45, 2.30),
        }).Value;

        return DeliveryZone.Create("Cotonou", "Cotonou", boundary, baseFee, extraFee, roadFactor).Value;
    }

    /// <summary>Grille moto de l'exemple : base 700, 120 F/km, express +500.</summary>
    private static PricingRule MotorcycleRule()
        => PricingRule.Create(
            "Moto", basePrice: 700m, pricePerKm: 120m, minimumPrice: 0m,
            vehicleType: VehicleType.Motorcycle, urgencySurcharge: 500m).Value;

    private static QuoteRequest Request(
        DeliveryType type = DeliveryType.Express,
        VehicleType vehicle = VehicleType.Motorcycle,
        decimal weightKg = 2.5m)
        => new(CahierPickup, CahierDropoff, Weight.FromKilograms(weightKg).Value,
               PackageSize.Small, type, vehicle);

    [Fact]
    public void Reproduit_exactement_l_exemple_du_cahier()
    {
        var result = PricingCalculator.Calculate(
            Request(), new[] { CotonouZone() }, new[] { MotorcycleRule() }, DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        var quote = result.Value;

        quote.DistanceKm.Should().Be(7.4);          // 3,38 km × 2,19, arrondi au dixième
        quote.BasePrice.Should().Be(700m);
        quote.DistancePrice.Should().Be(900m);      // 7,4 × 120 = 888 → arrondi à 900
        quote.ZoneSurcharge.Should().Be(0m);
        quote.VehicleSurcharge.Should().Be(0m);
        quote.WeightSurcharge.Should().Be(0m);
        quote.UrgencySurcharge.Should().Be(500m);
        quote.Total.Should().Be(2100m);
        quote.Currency.Should().Be("XOF");
    }

    [Fact]
    public void Les_composants_somment_toujours_au_total()
    {
        // C'est la promesse faite aux partenaires : ils peuvent refaire le calcul.
        // Un écart, même d'un franc, rendrait chaque devis contestable.
        var result = PricingCalculator.Calculate(
            Request(weightKg: 12m),
            new[] { CotonouZone(baseFee: 250m, extraFee: 150m) },
            new[] { WithWeightTiers(MotorcycleRule()) },
            DateTime.UtcNow);

        var q = result.Value;

        (q.BasePrice + q.DistancePrice + q.ZoneSurcharge
         + q.VehicleSurcharge + q.WeightSurcharge + q.UrgencySurcharge)
            .Should().Be(q.Total);
    }

    [Fact]
    public void Le_mode_standard_ne_paie_pas_la_surcharge_d_urgence()
    {
        var express = PricingCalculator.Calculate(
            Request(DeliveryType.Express), new[] { CotonouZone() }, new[] { AnyVehicleRule() }, DateTime.UtcNow).Value;

        var standard = PricingCalculator.Calculate(
            Request(DeliveryType.Standard), new[] { CotonouZone() }, new[] { AnyVehicleRule() }, DateTime.UtcNow).Value;

        express.UrgencySurcharge.Should().Be(500m);
        standard.UrgencySurcharge.Should().Be(0m);
        standard.Total.Should().Be(express.Total - 500m);
    }

    [Fact]
    public void Le_mode_programme_coute_le_prix_normal()
    {
        // Le client choisit son créneau : il ne paie pas pour attendre.
        var scheduled = PricingCalculator.Calculate(
            Request(DeliveryType.Scheduled), new[] { CotonouZone() }, new[] { AnyVehicleRule() }, DateTime.UtcNow).Value;

        scheduled.UrgencySurcharge.Should().Be(0m);
    }

    [Fact]
    public void Le_facteur_routier_retenu_est_le_plus_grand_des_deux_zones()
    {
        // Sous-estimer se paie sur chaque course et ne se voit jamais ;
        // surestimer se voit dans les devis refusés, et se corrige.
        var petiteZone = SmallZoneAround(CahierDropoff, roadFactor: 3.0, priority: 10);

        var result = PricingCalculator.Calculate(
            Request(), new[] { CotonouZone(roadFactor: 1.5), petiteZone },
            new[] { AnyVehicleRule() }, DateTime.UtcNow).Value;

        // 3,38 km × 3,0 ≈ 10,1 km — et non × 1,5.
        result.DistanceKm.Should().BeApproximately(10.1, 0.2);
    }

    [Fact]
    public void Le_plancher_s_applique_apres_la_somme()
    {
        var rule = PricingRule.Create(
            "Plancher", basePrice: 100m, pricePerKm: 10m, minimumPrice: 5000m,
            vehicleType: VehicleType.Motorcycle).Value;

        var q = PricingCalculator.Calculate(
            Request(DeliveryType.Standard), new[] { CotonouZone() }, new[] { rule }, DateTime.UtcNow).Value;

        q.Total.Should().BeGreaterThanOrEqualTo(5000m);

        // Le complément est versé sur la ligne de base, pour que la somme des
        // composants continue d'égaler le total.
        (q.BasePrice + q.DistancePrice + q.ZoneSurcharge
         + q.VehicleSurcharge + q.WeightSurcharge + q.UrgencySurcharge)
            .Should().Be(q.Total);
    }

    [Fact]
    public void Sans_grille_applicable_le_refus_est_metier_pas_technique()
    {
        // Un partenaire qui reçoit une erreur 500 réessaie ; un partenaire qui
        // reçoit un refus motivé adapte sa demande.
        var result = PricingCalculator.Calculate(
            Request(vehicle: VehicleType.Van), new[] { CotonouZone() },
            new[] { MotorcycleRule() }, DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pricing.no_rule");
    }

    [Fact]
    public void La_grille_la_plus_specifique_l_emporte()
    {
        var generale = PricingRule.Create("Générale", 500m, 100m).Value;
        var moto = PricingRule.Create(
            "Moto express", 700m, 120m,
            deliveryType: DeliveryType.Express, vehicleType: VehicleType.Motorcycle).Value;

        var q = PricingCalculator.Calculate(
            Request(), new[] { CotonouZone() }, new[] { generale, moto }, DateTime.UtcNow).Value;

        q.PricingRuleId.Should().Be(moto.Id.Value);
    }

    [Fact]
    public void Une_grille_expiree_ne_s_applique_plus()
    {
        var expiree = PricingRule.Create(
            "Ancienne", 700m, 120m,
            vehicleType: VehicleType.Motorcycle,
            validFromUtc: DateTime.UtcNow.AddDays(-10),
            validUntilUtc: DateTime.UtcNow.AddDays(-1)).Value;

        var result = PricingCalculator.Calculate(
            Request(), new[] { CotonouZone() }, new[] { expiree }, DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Hors_de_toute_zone_le_facteur_par_defaut_s_applique()
    {
        // Une course hors zone reste tarifable : on ne refuse pas une vente parce
        // qu'un polygone n'a pas encore été tracé.
        var result = PricingCalculator.Calculate(
            Request(DeliveryType.Standard), Array.Empty<DeliveryZone>(),
            new[] { AnyVehicleRule() }, DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        result.Value.DistanceKm.Should().BeApproximately(3.38 * DeliveryZone.DefaultRoadFactor, 0.2);
        result.Value.ZoneSurcharge.Should().Be(0m);
    }

    // ── Aides ───────────────────────────────────────────────────────────────

    private static PricingRule AnyVehicleRule()
        => PricingRule.Create("Tous véhicules", 700m, 120m, urgencySurcharge: 500m).Value;

    private static PricingRule WithWeightTiers(PricingRule rule)
    {
        rule.AddWeightTier(0m, 5m, 0m);
        rule.AddWeightTier(5m, 15m, 500m);
        rule.AddWeightTier(15m, 30m, 1000m);
        return rule;
    }

    private static DeliveryZone SmallZoneAround(Coordinates centre, double roadFactor, int priority)
    {
        const double d = 0.01;
        var boundary = GeoPolygon.Create(new[]
        {
            new GeoPoint(centre.Latitude - d, centre.Longitude - d),
            new GeoPoint(centre.Latitude - d, centre.Longitude + d),
            new GeoPoint(centre.Latitude + d, centre.Longitude + d),
            new GeoPoint(centre.Latitude + d, centre.Longitude - d),
        }).Value;

        return DeliveryZone.Create("Zone fine", "Cotonou", boundary, roadFactor: roadFactor, priority: priority).Value;
    }
}
