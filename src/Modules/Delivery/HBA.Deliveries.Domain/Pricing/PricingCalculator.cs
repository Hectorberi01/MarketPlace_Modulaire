using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Pricing;

/// <summary>
/// Une demande de devis.
///
/// <c>VehicleType</c> est FACULTATIF, et l'omettre est le chemin recommandé : le
/// moteur choisit alors le plus petit véhicule capable de porter le colis, qui est
/// aussi le moins cher. Un client n'a pas à connaître la charge utile d'un
/// tricycle pour obtenir le bon prix.
///
/// Le renseigner reste possible — un vendeur peut exiger une camionnette pour un
/// meuble fragile — mais engage alors l'appelant : un véhicule trop petit pour le
/// poids déclaré est REFUSÉ, jamais remplacé en silence. Changer de véhicule
/// change le prix, et personne ne doit découvrir ce changement sur la facture.
/// </summary>
public sealed record QuoteRequest(
    Coordinates Pickup,
    Coordinates Dropoff,
    Weight Weight,
    PackageSize Size,
    DeliveryType DeliveryType,
    VehicleType? VehicleType = null);

/// <summary>
/// Le détail du prix, ligne par ligne.
///
/// Chaque ligne est arrondie au multiple de 100, et leur somme EST le total : un
/// partenaire peut refaire le calcul à la main et retrouver notre chiffre. C'est
/// ce qui rend un devis opposable plutôt que discutable.
/// </summary>
public sealed record QuoteBreakdown(
    double DistanceKm,
    int EstimatedDurationMinutes,
    decimal BasePrice,
    decimal DistancePrice,
    decimal ZoneSurcharge,
    decimal VehicleSurcharge,
    decimal WeightSurcharge,
    decimal UrgencySurcharge,
    decimal Total,
    string Currency,
    Guid PricingRuleId,
    int PricingVersion,
    Guid? PickupZoneId,
    Guid? DropoffZoneId,

    // Le véhicule EFFECTIVEMENT retenu — celui demandé, ou celui choisi à défaut.
    // Il doit remonter jusqu'à l'appelant : sans lui, un client qui a laissé le
    // choix au moteur ne sait pas ce qu'il achète, et ne peut pas comprendre
    // pourquoi deux colis de poids voisins n'ont pas le même prix.
    VehicleType Vehicle);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE MOTEUR DE PRIX — FONCTION PURE.
///
/// Aucune base, aucun cache, aucune horloge implicite : on lui passe les zones,
/// les grilles et l'instant. C'est ce qui rend la tarification testable, et
/// surtout REJOUABLE : donné un devis contesté, on peut refaire exactement le
/// même calcul six mois plus tard.
///
/// LA DISTANCE EST ESTIMÉE, ET LE DOCUMENT LE DIT
///
/// On multiplie l'orthodromie par le facteur routier de la zone. Ce n'est pas un
/// itinéraire ; c'est une approximation calibrée. Entre deux zones de facteurs
/// différents, on retient le PLUS GRAND — parce qu'une sous-estimation se paie
/// sur chaque course et ne se voit jamais, tandis qu'une surestimation se voit
/// immédiatement dans les devis refusés, et se corrige.
///
/// LA DURÉE EST INDICATIVE
///
/// Vingt-deux kilomètres à l'heure de moyenne : c'est ce que fait une moto dans
/// Cotonou aux heures ouvrables, arrêts compris. Elle sert à afficher une
/// attente, jamais à facturer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class PricingCalculator
{
    /// <summary>Vitesse moyenne retenue pour l'estimation de durée, en km/h.</summary>
    public const double AverageSpeedKmh = 22.0;

    /// <summary>Temps fixe ajouté : approche du livreur, collecte, remise.</summary>
    public const int FixedOverheadMinutes = 8;

    public const string Currency = "XOF";

    public static Result<QuoteBreakdown> Calculate(
        QuoteRequest request,
        IReadOnlyList<DeliveryZone> zones,
        IReadOnlyList<PricingRule> rules,
        DateTime atUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pickupZone = ResolveZone(zones, request.Pickup);
        var dropoffZone = ResolveZone(zones, request.Dropoff);

        // Facteur routier : le plus grand des deux zones traversées. Voir l'encadré.
        var roadFactor = Math.Max(
            pickupZone?.RoadFactor ?? DeliveryZone.DefaultRoadFactor,
            dropoffZone?.RoadFactor ?? DeliveryZone.DefaultRoadFactor);

        var straightLineKm = request.Pickup.DistanceKmTo(request.Dropoff);

        var distanceResult = Distance.FromKilometers(straightLineKm * roadFactor);
        if (distanceResult.IsFailure)
        {
            return Result.Failure<QuoteBreakdown>(distanceResult.Error);
        }

        var distance = distanceResult.Value;

        var selection = SelectVehicleAndRule(request, rules, distance, atUtc);
        if (selection.IsFailure)
        {
            return Result.Failure<QuoteBreakdown>(selection.Error);
        }

        var (vehicle, rule) = selection.Value;

        var basePrice = Rounding.Up(rule.BasePrice);
        var distancePrice = Rounding.Up((decimal)distance.Kilometers * rule.PricePerKm);
        var zoneSurcharge = Rounding.Up((pickupZone?.BaseFee ?? 0m) + (dropoffZone?.ExtraFee ?? 0m));
        var weightSurcharge = Rounding.Up(rule.WeightSurchargeFor(request.Weight));

        // La surcharge d'urgence n'est due que pour l'express. Le mode programmé
        // coûte le prix normal : le client choisit son créneau, il ne paie pas
        // pour attendre.
        var urgencySurcharge = request.DeliveryType is DeliveryType.Express
            ? Rounding.Up(rule.UrgencySurcharge)
            : 0m;

        // Le supplément véhicule est porté par la grille elle-même — une grille
        // « camionnette » a son propre prix de base et son propre prix au
        // kilomètre. La ligne reste dans le détail pour que la réponse ait
        // toujours la même forme, ce que les intégrateurs préfèrent de loin à un
        // champ qui apparaît et disparaît.
        const decimal vehicleSurcharge = 0m;

        var total = basePrice + distancePrice + zoneSurcharge + vehicleSurcharge + weightSurcharge + urgencySurcharge;

        // Plancher : une course de 400 m coûte quand même le déplacement du
        // livreur. Il s'applique APRÈS la somme, sinon il ne planchérait rien.
        if (total < rule.MinimumPrice)
        {
            var complement = Rounding.Up(rule.MinimumPrice - total);
            basePrice += complement;
            total += complement;
        }

        var durationMinutes = FixedOverheadMinutes
            + (int)Math.Ceiling(distance.Kilometers / AverageSpeedKmh * 60);

        return new QuoteBreakdown(
            distance.Kilometers,
            durationMinutes,
            basePrice,
            distancePrice,
            zoneSurcharge,
            vehicleSurcharge,
            weightSurcharge,
            urgencySurcharge,
            total,
            Currency,
            rule.Id.Value,
            rule.Version,
            pickupZone?.Id.Value,
            dropoffZone?.Id.Value,
            vehicle);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUEL VÉHICULE, ET SOUS QUELLE GRILLE.
    ///
    /// LE DÉFAUT QUE CETTE MÉTHODE FERME
    ///
    /// La sélection de grille ne regardait que le type de service, le véhicule et
    /// la distance — jamais le POIDS face à la capacité du véhicule. Un devis moto
    /// pour un colis de 40 kg réussissait donc, au tarif moto. Puis le dispatch
    /// écartait toutes les motos, aucun autre véhicule n'était cherché, et la
    /// course mourait en « aucun livreur disponible ». Le client avait un prix, il
    /// avait payé, et personne ne venait.
    ///
    /// DEUX CHEMINS, DÉLIBÉRÉMENT DISSYMÉTRIQUES
    ///
    ///   • Véhicule OMIS : on choisit le plus petit capable qui dispose d'une
    ///     grille. Comme les grilles sont ordonnées dans le même sens que les
    ///     capacités, c'est aussi le moins cher. Les véhicules sans grille — vélo,
    ///     marche — sont traversés sans bruit : ils sont dispatchables mais leur
    ///     prix n'a pas été décidé.
    ///
    ///   • Véhicule IMPOSÉ mais trop petit : on REFUSE, on ne remplace pas. Un
    ///     vendeur qui exige une camionnette a une raison ; lui livrer autre chose
    ///     à un autre prix serait décider à sa place. Le message nomme le plus
    ///     petit véhicule qui conviendrait, pour qu'il puisse redemander.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static Result<(VehicleType Vehicle, PricingRule Rule)> SelectVehicleAndRule(
        QuoteRequest request,
        IReadOnlyList<PricingRule> rules,
        Distance distance,
        DateTime atUtc)
    {
        var weightKg = request.Weight.Kilograms;

        if (request.VehicleType is { } imposed)
        {
            if (!VehicleCapacity.CanCarry(imposed, weightKg))
            {
                var suggestion = VehicleCapacity.SmallestCapableOf(weightKg);

                return Result.Failure<(VehicleType, PricingRule)>(
                    Error.Validation("pricing.vehicle_too_small",
                        suggestion is null
                            ? $"Un colis de {weightKg:0.##} kg dépasse la capacité de tous nos véhicules."
                            : $"Un colis de {weightKg:0.##} kg dépasse la capacité d'un véhicule « {imposed} ». "
                              + $"Le plus petit véhicule adapté est « {suggestion} » — ou laissez le véhicule "
                              + "non renseigné pour que nous le choisissions."));
            }

            var imposedRule = SelectRule(rules, imposed, request.DeliveryType, distance, atUtc);

            return imposedRule is null
                ? Result.Failure<(VehicleType, PricingRule)>(NoRule(distance, imposed, request.DeliveryType))
                : (imposed, imposedRule);
        }

        // Véhicule omis : le premier capable ET tarifable, du plus petit au plus
        // grand. L'ordre de VehicleCapacity.ByIncreasingCapacity EST la règle.
        foreach (var candidate in VehicleCapacity.ByIncreasingCapacity)
        {
            if (!VehicleCapacity.CanCarry(candidate, weightKg))
            {
                continue;
            }

            var rule = SelectRule(rules, candidate, request.DeliveryType, distance, atUtc);
            if (rule is not null)
            {
                return (candidate, rule);
            }
        }

        // Aucun véhicule à la fois capable et tarifé. On distingue les deux causes :
        // « trop lourd pour notre flotte » n'appelle pas la même réaction que
        // « nous ne desservons pas cette course ».
        var plusPetitCapable = VehicleCapacity.SmallestCapableOf(weightKg);

        return plusPetitCapable is null
            ? Result.Failure<(VehicleType, PricingRule)>(
                Error.Validation("pricing.no_vehicle",
                    $"Un colis de {weightKg:0.##} kg dépasse la capacité de tous nos véhicules."))
            : Result.Failure<(VehicleType, PricingRule)>(
                NoRule(distance, plusPetitCapable.Value, request.DeliveryType));
    }

    /// <summary>
    /// Pas de grille applicable : ce n'est PAS une erreur technique. C'est « nous
    /// ne desservons pas cette course », et le message doit le dire clairement —
    /// un partenaire qui reçoit une erreur 500 réessaie ; un partenaire qui reçoit
    /// un refus motivé adapte sa demande.
    /// </summary>
    private static Error NoRule(Distance distance, VehicleType vehicle, DeliveryType deliveryType)
        => Error.NotFound("pricing.no_rule",
            $"Aucune grille tarifaire ne couvre une course de {distance} en {vehicle} ({deliveryType}).");

    /// <summary>
    /// Zone contenant le point. En cas de recouvrement, la plus grande priorité
    /// gagne — ce qui permet de poser une zone fine par-dessus une zone large.
    /// </summary>
    public static DeliveryZone? ResolveZone(IReadOnlyList<DeliveryZone> zones, Coordinates position)
        => zones
            .Where(z => z.Contains(position))
            .OrderByDescending(z => z.Priority)
            .ThenBy(z => z.Id.Value) // départage stable : deux appels donnent la même zone
            .FirstOrDefault();

    /// <summary>
    /// Grille applicable la plus spécifique. À spécificité égale, la plus
    /// récemment entrée en vigueur — c'est celle que l'on vient de publier.
    ///
    /// Prend un véhicule RÉSOLU et non la demande : quand l'appelant laisse le
    /// choix, cette méthode est interrogée une fois par véhicule candidat.
    /// </summary>
    public static PricingRule? SelectRule(
        IReadOnlyList<PricingRule> rules,
        VehicleType vehicle,
        DeliveryType deliveryType,
        Distance distance,
        DateTime atUtc)
        => rules
            .Where(r => r.AppliesTo(deliveryType, vehicle, distance, atUtc))
            .OrderByDescending(r => r.Specificity)
            .ThenByDescending(r => r.ValidFromUtc)
            .ThenBy(r => r.Id.Value)
            .FirstOrDefault();
}
