using FluentAssertions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Dispatch;
using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Domain.Pricing;
using HBA.Deliveries.Infrastructure.Persistence;
using Xunit;

namespace Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CHOIX AUTOMATIQUE DU VÉHICULE.
///
/// LE DÉFAUT QUE CES TESTS FERMENT
///
/// La sélection de grille ne regardait pas le poids face à la capacité du
/// véhicule. Un devis moto pour un colis de 40 kg réussissait, au tarif moto. Puis
/// le dispatch écartait toutes les motos, n'en cherchait pas d'autres, et la
/// course mourait en « aucun livreur disponible » après cinq tentatives vouées
/// d'avance. Le client avait un prix, il avait payé, personne ne venait.
///
/// Deux composants pris séparément étaient corrects. C'est leur DÉSACCORD qui
/// coûtait — et un désaccord ne se voit que dans un test qui les convoque
/// ensemble. Le dernier test de ce fichier fait exactement cela.
///
/// Les données utilisées sont celles qui partent réellement en production.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public class VehicleSelectionTests
{
    private static readonly IReadOnlyList<DeliveryZone> Zones = DeliveriesPricingSeeder.BuildZones().ToList();
    private static readonly IReadOnlyList<PricingRule> Rules = DeliveriesPricingSeeder.BuildRules().ToList();

    private static readonly Coordinates Pickup = Coordinates.Create(6.3703, 2.3912).Value;
    private static readonly Coordinates Dropoff = Coordinates.Create(6.3556, 2.4180).Value;

    /// <summary>
    /// <c>TheoryData</c> et non <c>InlineData</c> : les littéraux d'un
    /// <c>InlineData</c> sont des <c>double</c>, et leur conversion vers
    /// <c>decimal</c> se ferait en silence, à la limite près — précisément là où
    /// se jouent les cas 29,9 et 30,1.
    /// </summary>
    public static TheoryData<decimal, VehicleType> Poids => new()
    {
        { 2.5m, VehicleType.Motorcycle },    // le colis ordinaire
        { 29.9m, VehicleType.Motorcycle },   // juste sous la limite moto
        { 30.1m, VehicleType.Tricycle },     // juste au-dessus
        { 80m, VehicleType.Tricycle },
        { 149m, VehicleType.Tricycle },      // juste sous la limite tricycle
        { 160m, VehicleType.Car },           // le tricycle ne suit plus
        { 250m, VehicleType.Van },           // la voiture non plus
    };

    [Theory]
    [MemberData(nameof(Poids))]
    public void Le_moteur_retient_le_plus_petit_vehicule_capable(decimal poidsKg, VehicleType attendu)
    {
        var quote = Calculate(poidsKg, vehicule: null);

        quote.IsSuccess.Should().BeTrue();
        quote.Value.Vehicle.Should().Be(attendu);
    }

    [Fact]
    public void Un_vehicule_sans_grille_est_traverse_sans_bruit()
    {
        // Un colis de 2,5 kg tient sur un vélo, et le vélo vient AVANT la moto dans
        // l'ordre des capacités. Il n'a pourtant pas de grille tarifaire — c'est
        // délibéré. Le moteur doit donc l'enjamber et continuer, pas s'arrêter là.
        VehicleCapacity.CanCarry(VehicleType.Bicycle, 2.5m).Should().BeTrue();
        VehicleCapacity.ByIncreasingCapacity.ToList()
            .IndexOf(VehicleType.Bicycle)
            .Should().BeLessThan(VehicleCapacity.ByIncreasingCapacity.ToList().IndexOf(VehicleType.Motorcycle));

        Calculate(2.5m, vehicule: null).Value.Vehicle.Should().Be(VehicleType.Motorcycle);
    }

    [Fact]
    public void Le_choix_automatique_est_aussi_le_moins_cher()
    {
        // Ce n'est vrai que parce que les grilles sont ordonnées dans le même sens
        // que les capacités. Si quelqu'un rend un jour le tricycle moins cher que
        // la moto, « le plus petit capable » cesserait d'être « le moins cher » et
        // ce test tomberait — ce qui est le comportement voulu.
        var automatique = Calculate(2.5m, vehicule: null).Value;

        var plusGros = new[] { VehicleType.Tricycle, VehicleType.Car, VehicleType.Van }
            .Select(v => Calculate(2.5m, v).Value.Total);

        plusGros.Should().OnlyContain(total => total > automatique.Total);
    }

    [Fact]
    public void Un_vehicule_impose_trop_petit_est_refuse_et_non_remplace()
    {
        // Remplacer en silence changerait le prix sans que l'appelant l'ait
        // demandé. Un vendeur qui impose un véhicule a une raison ; on la respecte
        // ou on refuse, on ne décide pas à sa place.
        var result = Calculate(40m, VehicleType.Motorcycle);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pricing.vehicle_too_small");

        // Le message doit être ACTIONNABLE : il nomme le véhicule qui conviendrait.
        result.Error.Message.Should().Contain("Tricycle");
    }

    [Fact]
    public void Un_vehicule_impose_capable_est_respecte_meme_s_il_coute_plus_cher()
    {
        // Le meuble fragile qui doit voyager à plat.
        var quote = Calculate(2.5m, VehicleType.Van);

        quote.IsSuccess.Should().BeTrue();
        quote.Value.Vehicle.Should().Be(VehicleType.Van);
        quote.Value.Total.Should().BeGreaterThan(Calculate(2.5m, vehicule: null).Value.Total);
    }

    [Fact]
    public void Le_refus_pour_flotte_trop_petite_est_aujourd_hui_inatteignable()
    {
        // ⚠️ CE TEST DIT UNE CHOSE DÉSAGRÉABLE SUR DU CODE QUE JE VIENS D'ÉCRIRE.
        //
        // « pricing.no_vehicle » existe pour que la sélection reste une fonction
        // totale. Il est aujourd'hui INATTEIGNABLE : la camionnette n'a pas de
        // limite déclarée, et Weight plafonne à 300 kg — il existe donc toujours
        // un véhicule capable.
        //
        // Le documenter vaut mieux que d'écrire un test qui ferait semblant de le
        // couvrir. Le jour où la camionnette recevra une charge maximale, ce test
        // tombera et le refus deviendra réellement testable.
        VehicleCapacity.MaxWeightKg(VehicleType.Van).Should().BeNull();
        VehicleCapacity.SmallestCapableOf(Weight.MaxKilograms).Should().Be(VehicleType.Van);

        // Conséquence concrète : avec une flotte réduite aux deux-roues, un colis
        // de 300 kg est refusé pour absence de GRILLE, pas de véhicule. La nuance
        // compte — le premier se corrige en publiant un tarif, le second non.
        var result = PricingCalculator.Calculate(
            new QuoteRequest(Pickup, Dropoff, Weight.FromKilograms(Weight.MaxKilograms).Value,
                             PackageSize.Large, DeliveryType.Standard),
            Zones,
            Rules.Where(r => r.VehicleType == VehicleType.Motorcycle).ToList(),
            DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pricing.no_rule");
    }

    [Fact]
    public void Le_devis_fige_le_vehicule_choisi_et_non_celui_demande()
    {
        // request.VehicleType est nul quand on laisse le choix. Recopier la demande
        // enregistrerait « aucun véhicule » sur un devis pourtant chiffré, et
        // rendrait le calcul irrejouable — donc le montant indéfendable.
        var request = new QuoteRequest(
            Pickup, Dropoff, Weight.FromKilograms(80m).Value, PackageSize.Large, DeliveryType.Standard);

        request.VehicleType.Should().BeNull();

        var breakdown = PricingCalculator.Calculate(request, Zones, Rules, DateTime.UtcNow).Value;
        var quote = DeliveryQuote.FromBreakdown(request, breakdown);

        quote.VehicleType.Should().Be(VehicleType.Tricycle);
    }

    [Fact]
    public void Aucune_capacite_n_est_ecrite_deux_fois()
    {
        // Les 30 kg de la moto vivaient à la fois dans DeliveryPackage et dans la
        // politique de dispatch. Deux copies finissent toujours par diverger.
        DeliveryPackage.MotorcycleMaxWeightKg.Should().Be(VehicleCapacity.MotorcycleMaxKg);
    }

    [Fact]
    public void Un_vehicule_absent_du_bareme_de_capacite_fait_echouer_bruyamment()
    {
        // ⚠️ LE TEST LE PLUS IMPORTANT DE CE FICHIER POUR L'AVENIR.
        //
        // Le tricycle avait été ajouté à VehicleType sans être ajouté au dispatch,
        // et le « _ => false » final l'a rendu invisible pendant tout ce temps. Le
        // remplacement par une exception fait de l'oubli suivant un incident
        // immédiat au lieu d'une flotte fantôme.
        var inexistant = (VehicleType)999;

        var lever = () => VehicleCapacity.MaxWeightKg(inexistant);

        lever.Should().Throw<ArgumentOutOfRangeException>();

        // Et tous les véhicules déclarés, eux, DOIVENT avoir une capacité.
        foreach (var vehicule in Enum.GetValues<VehicleType>())
        {
            var lecture = () => VehicleCapacity.MaxWeightKg(vehicule);
            lecture.Should().NotThrow($"« {vehicule} » doit avoir une capacité déclarée");
        }
    }

    [Fact]
    public void Tout_devis_automatique_est_effectivement_dispatchable()
    {
        // ═════════════════════════════════════════════════════════════════════
        // LE TEST QUI FERME LA BOUCLE.
        //
        // Tarification et dispatch étaient corrects séparément ; c'est leur
        // désaccord qui produisait des courses payées et jamais livrées. Ce test
        // les convoque ensemble : pour chaque poids, le véhicule que la
        // TARIFICATION retient doit être un véhicule que le DISPATCH accepte.
        //
        // Il échouerait le jour où quelqu'un modifierait une capacité d'un seul
        // côté — ce qui est précisément ce qui s'était produit.
        // ═════════════════════════════════════════════════════════════════════
        decimal[] poids = [0.5m, 2.5m, 15m, 29.9m, 30.1m, 80m, 149m, 160m, 200m, 250m];

        foreach (var kg in poids)
        {
            var quote = Calculate(kg, vehicule: null);
            quote.IsSuccess.Should().BeTrue($"un colis de {kg} kg doit être tarifable");

            var livreur = Driver.Register(
                Guid.NewGuid(), "Livreur", "+229 01 97 24 18 06", quote.Value.Vehicle).Value;
            livreur.Verify();
            livreur.GoOnline();

            var colis = DeliveryPackage.Create("Colis", kg).Value;

            DispatchPolicy.CanCarry(livreur, colis).Should().BeTrue(
                $"le devis retient « {quote.Value.Vehicle} » pour {kg} kg : "
                + "le dispatch doit accepter ce véhicule, sinon la course est payée et jamais livrée");
        }
    }

    // ── Aides ───────────────────────────────────────────────────────────────

    private static Marketplace.BuildingBlocks.Domain.Results.Result<QuoteBreakdown> Calculate(
        decimal weightKg, VehicleType? vehicule)
        => PricingCalculator.Calculate(
            new QuoteRequest(
                Pickup, Dropoff, Weight.FromKilograms(weightKg).Value,
                PackageSize.Medium, DeliveryType.Standard, vehicule),
            Zones, Rules, DateTime.UtcNow);
}
