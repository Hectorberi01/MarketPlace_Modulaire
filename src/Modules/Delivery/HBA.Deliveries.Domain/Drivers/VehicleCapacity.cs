namespace HBA.Deliveries.Domain.Drivers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CHAQUE VÉHICULE PEUT PORTER — ÉCRIT UNE SEULE FOIS.
///
/// POURQUOI CE FICHIER EXISTE
///
/// Ces nombres vivaient à trois endroits qui ne se parlaient pas : les 30 kg de
/// la moto dans <c>DeliveryPackage</c>, les 150 kg du tricycle dans
/// <c>DispatchPolicy</c>, et les paliers de poids des grilles tarifaires. Rien ne
/// garantissait leur accord.
///
/// Le désaccord avait une conséquence précise et coûteuse : la tarification
/// acceptait de chiffrer un colis de 40 kg en moto — aucune règle ne vérifiait la
/// capacité — puis le dispatch écartait TOUTES les motos. Le client obtenait un
/// prix, payait, et aucun livreur ne pouvait jamais venir. La course finissait en
/// « aucun livreur disponible » après cinq tentatives vouées d'avance.
///
/// Un devis qui promet ce que le dispatch refuse est pire qu'un refus.
///
/// LES CAPACITÉS SONT ORDONNÉES, ET C'EST UTILISÉ
///
/// <see cref="ByIncreasingCapacity"/> permet de choisir le PLUS PETIT véhicule
/// capable de porter un colis. Comme les grilles tarifaires sont ordonnées dans
/// le même sens — moto moins chère que tricycle, moins cher que voiture, moins
/// chère que camionnette — le plus petit véhicule capable est aussi le moins
/// cher. Le client n'a donc pas à connaître nos capacités de charge pour obtenir
/// le bon prix : c'est notre métier, pas le sien.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class VehicleCapacity
{
    /// <summary>À pied : ce qu'on porte sans se blesser sur quelques centaines de mètres.</summary>
    public const decimal OnFootMaxKg = 5m;

    /// <summary>Vélo : un panier avant, pas davantage.</summary>
    public const decimal BicycleMaxKg = 10m;

    /// <summary>
    /// Moto : le seuil vient de l'usage. À Cotonou, la quasi-totalité de la flotte
    /// est en deux-roues, et au-delà de 30 kg la conduite devient dangereuse.
    /// </summary>
    public const decimal MotorcycleMaxKg = 30m;

    /// <summary>
    /// Tricycle à moteur : la charge utile courante au Bénin. C'est exactement la
    /// place que ce véhicule occupe — entre la moto et la camionnette, dans des
    /// ruelles où une voiture ne passe pas.
    /// </summary>
    public const decimal TricycleMaxKg = 150m;

    /// <summary>
    /// Voiture : coffre et banquette arrière. Borne ajoutée avec ce fichier — la
    /// voiture était jusqu'ici réputée tout porter, ce qui l'aurait fait choisir à
    /// la place d'une camionnette pour un chargement qui n'y entre pas.
    /// </summary>
    public const decimal CarMaxKg = 200m;

    /// <summary>
    /// Du plus petit au plus grand. L'ordre EST la règle de choix : il ne doit
    /// jamais être réarrangé pour des raisons de lisibilité.
    /// </summary>
    public static IReadOnlyList<VehicleType> ByIncreasingCapacity { get; } =
    [
        VehicleType.OnFoot,
        VehicleType.Bicycle,
        VehicleType.Motorcycle,
        VehicleType.Tricycle,
        VehicleType.Car,
        VehicleType.Van
    ];

    /// <summary>
    /// Charge maximale. <c>null</c> = pas de limite utile : une camionnette porte
    /// plus que ce que le type <c>Weight</c> accepte de déclarer (300 kg).
    /// </summary>
    public static decimal? MaxWeightKg(VehicleType vehicle) => vehicle switch
    {
        VehicleType.OnFoot => OnFootMaxKg,
        VehicleType.Bicycle => BicycleMaxKg,
        VehicleType.Motorcycle => MotorcycleMaxKg,
        VehicleType.Tricycle => TricycleMaxKg,
        VehicleType.Car => CarMaxKg,
        VehicleType.Van => null,

        // ⚠️ PAS DE « _ => null ». Un véhicule ajouté à l'énumération et oublié ici
        // doit CASSER, bruyamment, à la première utilisation. Le renvoyer comme
        // « sans limite » le laisserait accepter n'importe quelle charge ; le
        // renvoyer comme « zéro » le rendrait invisible au dispatch, en silence.
        // Les deux oublis ont déjà eu lieu dans ce module.
        _ => throw new ArgumentOutOfRangeException(
            nameof(vehicle), vehicle,
            "Capacité de charge non définie pour ce véhicule : ajoutez-la à VehicleCapacity.")
    };

    /// <summary>
    /// Vélo et marche exigent un poids DÉCLARÉ. Ailleurs, un poids inconnu est
    /// présumé transportable : personne ne pèse un repas, et refuser faute de
    /// déclaration immobiliserait la flotte pour la majorité des courses. Sur un
    /// vélo, en revanche, l'inconnu est un risque physique.
    /// </summary>
    public static bool RequiresDeclaredWeight(VehicleType vehicle)
        => vehicle is VehicleType.OnFoot or VehicleType.Bicycle;

    /// <summary>Ce véhicule peut-il porter ce colis ? <paramref name="weightKg"/> nul = non déclaré.</summary>
    public static bool CanCarry(VehicleType vehicle, decimal? weightKg)
    {
        if (weightKg is null)
        {
            return !RequiresDeclaredWeight(vehicle);
        }

        var max = MaxWeightKg(vehicle);
        return max is null || weightKg <= max;
    }

    /// <summary>
    /// Le plus petit véhicule capable de porter ce poids, parmi ceux proposés.
    /// Nul si aucun ne convient.
    /// </summary>
    public static VehicleType? SmallestCapableOf(decimal? weightKg)
    {
        foreach (var vehicle in ByIncreasingCapacity)
        {
            if (CanCarry(vehicle, weightKg))
            {
                return vehicle;
            }
        }

        return null;
    }
}
