using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;

namespace HBA.Deliveries.Domain.Dispatch;

/// <summary>Un livreur candidat, avec sa position au moment de la recherche.</summary>
/// <param name="Driver">Le livreur.</param>
/// <param name="Position">Sa position courante, issue du cache.</param>
public readonly record struct DriverCandidate(Driver Driver, Coordinates Position);

/// <summary>Un candidat retenu, avec sa note et le détail qui l'explique.</summary>
public readonly record struct ScoredDriver(DriverCandidate Candidate, double Score, double DistanceKm)
{
    public DriverId DriverId => Candidate.Driver.Id;
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// À QUI PROPOSER LA COURSE.
///
/// Fonction PURE : aucune base, aucun cache, aucune horloge. Elle prend une liste
/// de candidats et rend un classement. C'est ce qui la rend testable — et le
/// dispatch est précisément la partie qu'il faut pouvoir régler sans rien déployer.
///
/// TROIS CRITÈRES, ET POURQUOI PAS QUATRE
///
///   • LA DISTANCE domine. Un livreur à 800 m arrive avant un livreur à 3 km,
///     quelle que soit son ancienneté. C'est la seule chose que le client ressent.
///   • L'EXPÉRIENCE départage à distance comparable. Un livreur qui a mené cent
///     courses connaît les quartiers ; c'est un vrai avantage là où les rues n'ont
///     pas de nom.
///   • LE VÉHICULE est éliminatoire, pas pondéré : un colis de 40 kg ne part pas
///     à moto. Un critère physique ne se négocie pas contre de la proximité.
///
/// Ce qu'on ne fait PAS : pondérer par la nature du contenu. Le moteur logistique
/// ignore ce qu'il transporte — voir <see cref="Deliveries.DeliverySource"/>.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DispatchPolicy
{
    /// <summary>
    /// Rayon de recherche par défaut. Au-delà, le temps d'approche dépasse le
    /// temps de course et l'attente devient perceptible pour le client.
    /// </summary>
    public const double DefaultRadiusKm = 5.0;

    /// <summary>Rayon élargi, utilisé après un premier tour infructueux.</summary>
    public const double ExtendedRadiusKm = 12.0;


    /// <summary>Au-delà, l'expérience ne départage plus : cent courses ou mille, c'est pareil.</summary>
    private const int ExperiencePlateau = 100;

    /// <summary>Poids de l'expérience dans la note finale. Volontairement modeste.</summary>
    private const double ExperienceWeight = 0.20;

    /// <summary>
    /// Classe les candidats, du meilleur au moins bon. Les inéligibles sont écartés,
    /// pas mal notés : un livreur hors ligne ou dont la moto ne peut pas porter le
    /// colis n'est pas « un mauvais choix », il n'est pas un choix.
    /// </summary>
    /// <param name="delivery">La course à pourvoir.</param>
    /// <param name="candidates">Livreurs trouvés dans le rayon.</param>
    /// <param name="radiusKm">Rayon retenu pour ce tour.</param>
    public static IReadOnlyList<ScoredDriver> Rank(
        Delivery delivery,
        IEnumerable<DriverCandidate> candidates,
        double radiusKm = DefaultRadiusKm)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(candidates);

        // La distance se mesure jusqu'au point de COLLECTE, pas de remise : ce qui
        // retarde la course, c'est le temps qu'un livreur met à venir chercher le
        // colis. La distance jusqu'au client est la même pour tout le monde.
        var origin = delivery.Pickup.Position;

        var alreadyRefused = delivery.Assignments
            .Where(a => a.Outcome is AssignmentOutcome.Rejected)
            .Select(a => a.DriverId)
            .ToHashSet();

        var scored = new List<ScoredDriver>();

        foreach (var candidate in candidates)
        {
            if (!candidate.Driver.CanReceiveOffers)
            {
                continue;
            }

            // Ne jamais reproposer à quelqu'un qui a déjà dit non. L'agrégat le
            // refuserait de toute façon ; l'écarter ici évite un tour pour rien.
            if (alreadyRefused.Contains(candidate.Driver.Id))
            {
                continue;
            }

            if (!CanCarry(candidate.Driver, delivery.Package))
            {
                continue;
            }

            var distanceKm = origin.DistanceKmTo(candidate.Position);
            if (distanceKm > radiusKm)
            {
                continue;
            }

            // Proximité ramenée entre 0 et 1 : 1 sur place, 0 à la limite du rayon.
            var proximity = 1.0 - (distanceKm / radiusKm);
            var score = proximity * (1.0 - ExperienceWeight) + Experience(candidate.Driver) * ExperienceWeight;

            scored.Add(new ScoredDriver(candidate, score, distanceKm));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.DriverId.Value) // départage stable : deux appels donnent le même ordre
            .ToList();
    }

    /// <summary>Le véhicule peut-il porter ce colis ? Critère physique, éliminatoire.</summary>
    public static bool CanCarry(Driver driver, DeliveryPackage package)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(package);

        // ─────────────────────────────────────────────────────────────────────
        // CETTE MÉTHODE FUT UN « switch » AVEC UN « _ => false » FINAL, ET CE
        // REPLI A COÛTÉ CHER.
        //
        // Tricycle avait été ajouté à VehicleType sans être ajouté au switch : il
        // tombait donc dans le repli. Un tricycle enregistré, vérifié, en ligne,
        // garé devant la boutique, n'aurait JAMAIS reçu de course — sans erreur,
        // sans journal, sans rien. Le livreur en aurait conclu qu'il n'y a pas de
        // demande.
        //
        // Le défaut n'était pas l'oubli, qui arrive : c'était le « _ => false »,
        // qui traite un oubli comme une décision. VehicleCapacity lève désormais
        // une exception sur un véhicule inconnu — bruyamment, à la première
        // utilisation.
        //
        // Les capacités elles-mêmes vivent maintenant en UN SEUL endroit, partagé
        // avec la tarification. C'est ce qui garantit qu'un devis ne promet plus
        // ce que le dispatch refusera.
        // ─────────────────────────────────────────────────────────────────────
        return VehicleCapacity.CanCarry(driver.Vehicle, package.WeightKg);
    }

    private static double Experience(Driver driver)
        => Math.Min(driver.CompletedDeliveries, ExperiencePlateau) / (double)ExperiencePlateau;
}
