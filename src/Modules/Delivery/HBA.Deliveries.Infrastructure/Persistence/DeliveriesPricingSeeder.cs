using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Domain.Pricing;
using Microsoft.EntityFrameworkCore;

namespace HBA.Deliveries.Infrastructure.Persistence;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ZONES ET GRILLES TARIFAIRES DE DÉPART.
///
/// POURQUOI CET AMORÇAGE N'EST PAS UN JEU DE DÉMONSTRATION
///
/// Sans une zone et une grille en base, <c>PricingCalculator</c> ne trouve aucune
/// règle applicable et renvoie <c>pricing.no_rule</c>. Autrement dit : tant que
/// cette table est vide, HBA Delivery accepte les demandes, calcule une distance,
/// et refuse TOUS les devis. Le module est complet et parfaitement inutile.
///
/// C'est donc de la CONFIGURATION D'EXPLOITATION, au même titre que les rôles RBAC
/// de <c>IdentityDataSeeder</c> — pas des données de vitrine.
///
/// ⚠️ CE SONT DES PRIX. LA RÈGLE QUI COMPTE EST : NE JAMAIS ÉCRASER.
///
/// Chaque élément est créé UNIQUEMENT s'il n'existe pas déjà, reconnu par son nom.
/// Rien n'est jamais mis à jour. La raison est directe : le jour où l'exploitation
/// porte le kilomètre moto de 120 à 130 FCFA, un redéploiement ne doit pas le
/// ramener à 120. Ce serait une perte de recette silencieuse, découverte des
/// semaines plus tard sur un rapprochement comptable — la même famille de bogue
/// que la réinitialisation du mot de passe administrateur, déjà écartée ailleurs
/// dans ce dépôt pour la même raison.
///
/// Pour CHANGER un tarif, on passe donc par l'administration, jamais par ce
/// fichier. Modifier les valeurs ci-dessous n'a d'effet que sur une base neuve.
///
/// LES MONTANTS SONT UN POINT DE DÉPART, PAS UNE VÉRITÉ
///
/// Ils sont plausibles pour le grand Cotonou en 2026 et cohérents avec l'exemple
/// du cahier d'architecture. Ils n'ont PAS été validés commercialement. Ils sont
/// journalisés au démarrage, chiffre par chiffre, précisément pour que personne
/// ne les découvre sur une facture.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DeliveriesPricingSeeder
{
    /// <summary>Ce que l'amorçage a réellement fait — à journaliser par l'appelant.</summary>
    /// <param name="ZonesCreated">Noms des zones créées.</param>
    /// <param name="RulesCreated">Noms des grilles créées.</param>
    /// <param name="AlreadyPresent">Éléments trouvés en base, laissés intacts.</param>
    public sealed record SeedReport(
        IReadOnlyList<string> ZonesCreated,
        IReadOnlyList<string> RulesCreated,
        IReadOnlyList<string> AlreadyPresent)
    {
        public bool DidNothing => ZonesCreated.Count == 0 && RulesCreated.Count == 0;
    }

    public static async Task<SeedReport> SeedAsync(
        DeliveriesDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var zonesCreated = new List<string>();
        var rulesCreated = new List<string>();
        var alreadyPresent = new List<string>();

        foreach (var zone in BuildZones())
        {
            if (await dbContext.DeliveryZones.AnyAsync(z => z.Name == zone.Name, cancellationToken))
            {
                alreadyPresent.Add($"zone « {zone.Name} »");
                continue;
            }

            await dbContext.DeliveryZones.AddAsync(zone, cancellationToken);
            zonesCreated.Add($"{zone.Name} (facteur routier {zone.RoadFactor:0.00})");
        }

        foreach (var rule in BuildRules())
        {
            if (await dbContext.PricingRules.AnyAsync(r => r.Name == rule.Name, cancellationToken))
            {
                alreadyPresent.Add($"grille « {rule.Name} »");
                continue;
            }

            await dbContext.PricingRules.AddAsync(rule, cancellationToken);
            rulesCreated.Add($"{rule.Name} ({rule.BasePrice:0} F + {rule.PricePerKm:0} F/km, plancher {rule.MinimumPrice:0} F)");
        }

        if (zonesCreated.Count > 0 || rulesCreated.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new SeedReport(zonesCreated, rulesCreated, alreadyPresent);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LES ZONES
    //
    // Des rectangles, volontairement. Un contour fidèle au quartier demanderait
    // un relevé de terrain que personne n'a fait, et donnerait une fausse
    // impression d'exactitude. Un rectangle annonce ce qu'il est : une
    // approximation à corriger depuis l'administration, une fois de vraies
    // courses livrées.
    //
    // LES PRIORITÉS SONT TOUTES DISTINCTES. À priorité égale, la zone retenue
    // serait départagée par un GUID tiré au hasard : deux installations
    // factureraient différemment la même course, sans que rien ne l'explique.
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Public — et non privé — pour être ÉPROUVÉ. Ces contours sont vingt
    /// coordonnées saisies à la main : une inversion latitude/longitude, une
    /// décimale déplacée, et la zone se retrouve dans le golfe de Guinée sans que
    /// rien ne proteste. Le devis tomberait alors sur le facteur routier par
    /// défaut, et la course serait sous-facturée en silence.
    /// </summary>
    public static IEnumerable<DeliveryZone> BuildZones()
    {
        // Le seul facteur MESURÉ : l'exemple du cahier d'architecture donne
        // 3,38 km à vol d'oiseau pour 7,4 km de route entre deux points du centre.
        yield return Zone(
            name: "Cotonou — centre",
            south: 6.345, north: 6.385, west: 2.380, east: 2.440,
            roadFactor: 2.19,
            priority: 50);

        // Au-delà du chenal. Tout passage se fait par un pont : le détour est
        // structurel, pas conjoncturel.
        yield return Zone(
            name: "Cotonou — Akpakpa",
            south: 6.340, north: 6.375, west: 2.440, east: 2.490,
            roadFactor: 2.40,
            extraFee: 100m,
            priority: 40);

        // Littoral ouest. Peu de voies traversantes, mais pas de coupure majeure.
        yield return Zone(
            name: "Cotonou — Fidjrossè / Cadjèhoun",
            south: 6.340, north: 6.375, west: 2.330, east: 2.380,
            roadFactor: 1.90,
            priority: 30);

        // Corridor de la RNIE 1 : l'axe est droit, le facteur chute.
        // Le supplément d'arrivée paie le RETOUR À VIDE — un livreur qui monte à
        // Godomey ne trouve pas de course pour redescendre.
        yield return Zone(
            name: "Godomey",
            south: 6.355, north: 6.400, west: 2.290, east: 2.335,
            roadFactor: 1.60,
            extraFee: 200m,
            priority: 20);

        yield return Zone(
            name: "Abomey-Calavi — centre",
            south: 6.415, north: 6.480, west: 2.325, east: 2.390,
            roadFactor: 1.70,
            extraFee: 300m,
            priority: 10);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LES GRILLES
    //
    // ⚠️ UNE GRILLE PAR VÉHICULE, ET AUCUNE GRILLE « FOURRE-TOUT ».
    //
    // Une grille sans contrainte de véhicule attraperait tout ce qui n'a pas la
    // sienne — et facturerait une camionnette au tarif moto, en silence. Le refus
    // « pricing.no_rule » est préférable : il est visible, il est motivé, et il se
    // corrige en ajoutant la grille manquante.
    //
    // CONSÉQUENCE ASSUMÉE : le vélo et la marche n'ont pas de grille. Ils sont
    // dispatchables mais non tarifables, donc invendables tant que quelqu'un n'a
    // pas décidé de leur prix. C'est voulu — inventer ici un tarif vélo serait
    // décider d'un prix à la place de l'exploitation.
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>Public pour la même raison que <see cref="BuildZones"/> : pour être éprouvé.</summary>
    public static IEnumerable<PricingRule> BuildRules()
    {
        // La grille de l'exemple du cahier. À ne pas modifier sans refaire tourner
        // PricingCalculatorTests : un test y vérifie 7,4 km → 2 100 FCFA.
        yield return Rule(
            name: "Moto — standard",
            basePrice: 700m, pricePerKm: 120m, minimumPrice: 1000m,
            vehicle: VehicleType.Motorcycle, urgencySurcharge: 500m,
            tiers: [(0m, 5m, 0m), (5m, 15m, 500m), (15m, 30m, 1000m)]);

        // ─────────────────────────────────────────────────────────────────────
        // LE TRICYCLE — GRILLE AJOUTÉE PARCE QUE SON ABSENCE ÉTAIT LE MÊME PIÈGE
        // QUE CELUI TROUVÉ DANS LE DISPATCH.
        //
        // VehicleType.Tricycle existait, le dispatch l'ignorait silencieusement
        // (corrigé). Sans grille, il resterait tout aussi silencieusement
        // intarifable : chaque devis tricycle répondrait « pricing.no_rule », et
        // on en conclurait que la demande n'existe pas.
        //
        // Un trou corrigé d'un côté seulement n'est pas un trou corrigé.
        //
        // Positionné entre la moto et la voiture, ce qui est exactement sa place
        // au Bénin : jusqu'à 150 kg, dans des ruelles où une voiture ne passe pas.
        // ─────────────────────────────────────────────────────────────────────
        yield return Rule(
            name: "Tricycle — standard",
            basePrice: 1000m, pricePerKm: 150m, minimumPrice: 1500m,
            vehicle: VehicleType.Tricycle, urgencySurcharge: 700m,
            tiers: [(0m, 30m, 0m), (30m, 80m, 700m), (80m, 150m, 1500m)]);

        yield return Rule(
            name: "Voiture — standard",
            basePrice: 1500m, pricePerKm: 200m, minimumPrice: 2000m,
            vehicle: VehicleType.Car, urgencySurcharge: 1000m,
            tiers: [(0m, 20m, 0m), (20m, 50m, 500m), (50m, 100m, 1500m)]);

        yield return Rule(
            name: "Camionnette — standard",
            basePrice: 2500m, pricePerKm: 300m, minimumPrice: 3500m,
            vehicle: VehicleType.Van, urgencySurcharge: 1500m,
            // Dernier palier NON BORNÉ : borner à 500 kg créerait une plage morte,
            // Weight refusant déjà tout ce qui dépasse 300 kg. Un palier qu'aucun
            // colis ne peut atteindre a l'air d'une règle et n'en est pas une.
            tiers: [(0m, 50m, 0m), (50m, 150m, 1000m), (150m, null, 3000m)]);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static DeliveryZone Zone(
        string name,
        double south, double north, double west, double east,
        double roadFactor,
        int priority,
        decimal baseFee = 0m,
        decimal extraFee = 0m)
    {
        var boundary = GeoPolygon.Create(
        [
            new GeoPoint(south, west),
            new GeoPoint(south, east),
            new GeoPoint(north, east),
            new GeoPoint(north, west),
        ]);

        if (boundary.IsFailure)
        {
            // Impossible avec des constantes littérales : ce serait une faute de
            // frappe dans ce fichier, pas une donnée d'exploitation. On refuse de
            // démarrer plutôt que de semer une zone à moitié définie.
            throw new InvalidOperationException(
                $"Contour de la zone « {name} » invalide : {boundary.Error.Message}");
        }

        var zone = DeliveryZone.Create(name, "Grand Cotonou", boundary.Value, baseFee, extraFee, roadFactor, priority);
        if (zone.IsFailure)
        {
            throw new InvalidOperationException(
                $"Zone « {name} » invalide : {zone.Error.Message}");
        }

        return zone.Value;
    }

    private static PricingRule Rule(
        string name,
        decimal basePrice,
        decimal pricePerKm,
        decimal minimumPrice,
        VehicleType vehicle,
        decimal urgencySurcharge,
        (decimal From, decimal? To, decimal Surcharge)[] tiers)
    {
        var rule = PricingRule.Create(
            name, basePrice, pricePerKm, minimumPrice,
            vehicleType: vehicle, urgencySurcharge: urgencySurcharge);

        if (rule.IsFailure)
        {
            throw new InvalidOperationException($"Grille « {name} » invalide : {rule.Error.Message}");
        }

        foreach (var (from, to, surcharge) in tiers)
        {
            var tier = rule.Value.AddWeightTier(from, to, surcharge);
            if (tier.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Palier {from}–{to} kg de la grille « {name} » refusé : {tier.Error.Message}");
            }
        }

        return rule.Value;
    }
}
