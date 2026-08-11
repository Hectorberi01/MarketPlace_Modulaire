using HBA.Deliveries.Domain.Deliveries;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Pricing;

/// <summary>Identité forte d'une zone.</summary>
public readonly record struct DeliveryZoneId(Guid Value)
{
    public static DeliveryZoneId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE ZONE DE LIVRAISON — UN CONTOUR, PAS UNE COMMUNE.
///
/// POURQUOI LE RÉFÉRENTIEL DES 77 COMMUNES NE SUFFIT PAS
///
/// Il existe déjà, il est fiable, et il sert aux adresses. Mais Cotonou y est
/// UNE seule entrée : impossible d'y distinguer le centre d'Akpakpa ou de
/// Fidjrossè, alors que ce sont précisément ces écarts que le prix doit refléter.
/// La commune reste la bonne unité pour dire OÙ l'on habite ; elle est trop
/// grossière pour dire COMBIEN coûte d'y aller.
///
/// LE FACTEUR ROUTIER VIT ICI, ET C'EST LE CHAMP LE PLUS IMPORTANT
///
/// Entre les deux points de l'exemple du cahier, l'orthodromie fait 3,4 km et la
/// route 7,4 : un facteur de 2,19. Ce coefficient n'est pas une constante du
/// monde — il dépend de la lagune, des sens uniques, des ponts. Il appartient
/// donc à la zone, et il se CALIBRE : à mesure que de vraies courses sont
/// livrées, on compare la distance réelle à l'orthodromie et on ajuste.
///
/// C'est une approximation assumée. Elle a un avantage décisif sur un service
/// d'itinéraire : elle est reproductible, gratuite, et ne tombe jamais en panne.
/// Le jour où un devis dépend d'une API tierce, l'indisponibilité de celle-ci
/// devient une interruption de vente.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeliveryZone : AggregateRoot<DeliveryZoneId>
{
    /// <summary>Facteur retenu à défaut : une route fait rarement moins de 1,3 fois le vol d'oiseau.</summary>
    public const double DefaultRoadFactor = 1.4;

    private DeliveryZone(
        DeliveryZoneId id,
        string name,
        string city,
        GeoPolygon boundary,
        decimal baseFee,
        decimal extraFee,
        double roadFactor,
        int priority)
        : base(id)
    {
        Name = name;
        City = city;
        Boundary = boundary;
        BaseFee = baseFee;
        ExtraFee = extraFee;
        RoadFactor = roadFactor;
        Priority = priority;
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    // Requis par EF Core.
    private DeliveryZone()
    {
        Name = string.Empty;
        City = string.Empty;
        Boundary = null!;
    }

    public string Name { get; private set; }

    /// <summary>Ville de rattachement, pour regrouper à l'affichage.</summary>
    public string City { get; private set; }

    public GeoPolygon Boundary { get; private set; }

    /// <summary>Supplément appliqué quand la course PART de cette zone.</summary>
    public decimal BaseFee { get; private set; }

    /// <summary>Supplément appliqué quand la course ARRIVE dans cette zone.</summary>
    public decimal ExtraFee { get; private set; }

    /// <summary>
    /// Rapport entre distance routière et distance à vol d'oiseau. Se calibre
    /// avec les courses réellement effectuées.
    /// </summary>
    public double RoadFactor { get; private set; }

    /// <summary>
    /// Départage les zones qui se recouvrent. La plus GRANDE priorité gagne.
    ///
    /// Le recouvrement n'est pas un défaut : deux quartiers voisins tracés à la
    /// main se chevauchent toujours de quelques mètres, et exiger un pavage
    /// parfait rendrait la saisie impossible. La priorité permet en outre de
    /// poser une zone fine (« aéroport ») par-dessus une zone large (« Cotonou »).
    /// </summary>
    public int Priority { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static Result<DeliveryZone> Create(
        string? name,
        string? city,
        GeoPolygon boundary,
        decimal baseFee = 0m,
        decimal extraFee = 0m,
        double roadFactor = DefaultRoadFactor,
        int priority = 0)
    {
        var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (trimmedName is null)
        {
            return Result.Failure<DeliveryZone>(
                Error.Validation("pricing.zone.name_required", "Le nom de la zone est requis."));
        }

        if (boundary is null)
        {
            return Result.Failure<DeliveryZone>(
                Error.Validation("pricing.zone.boundary_required", "Le contour de la zone est requis."));
        }

        if (baseFee < 0 || extraFee < 0)
        {
            return Result.Failure<DeliveryZone>(
                Error.Validation("pricing.zone.fee_negative", "Un supplément de zone ne peut pas être négatif."));
        }

        // Un facteur inférieur à 1 signifierait que la route est plus courte que
        // la ligne droite. C'est géométriquement impossible, et c'est la faute de
        // saisie la plus probable — une virgule au mauvais endroit.
        if (roadFactor < 1.0 || roadFactor > 5.0)
        {
            return Result.Failure<DeliveryZone>(
                Error.Validation("pricing.zone.road_factor_invalid",
                    "Le facteur routier doit être compris entre 1 et 5 : en dessous de 1, la route serait plus courte que la ligne droite."));
        }

        return new DeliveryZone(
            DeliveryZoneId.New(), trimmedName, city?.Trim() ?? string.Empty,
            boundary, baseFee, extraFee, roadFactor, priority);
    }

    public bool Contains(Coordinates position) => IsActive && Boundary.Contains(position);

    public Result UpdateFees(decimal baseFee, decimal extraFee)
    {
        if (baseFee < 0 || extraFee < 0)
        {
            return Result.Failure(Error.Validation("pricing.zone.fee_negative", "Un supplément ne peut pas être négatif."));
        }

        BaseFee = baseFee;
        ExtraFee = extraFee;
        return Result.Success();
    }

    /// <summary>Ajuste le facteur routier — l'opération de calibrage.</summary>
    public Result Recalibrate(double roadFactor)
    {
        if (roadFactor < 1.0 || roadFactor > 5.0)
        {
            return Result.Failure(
                Error.Validation("pricing.zone.road_factor_invalid", "Le facteur routier doit être compris entre 1 et 5."));
        }

        RoadFactor = roadFactor;
        return Result.Success();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

/// <summary>Accès aux zones.</summary>
public interface IDeliveryZoneRepository
{
    /// <summary>
    /// Toutes les zones actives. Volontairement SANS filtre géographique : elles
    /// se comptent en dizaines, tiennent en cache, et le test d'appartenance se
    /// fait en mémoire. Une requête par point serait un aller-retour de plus sur
    /// le chemin du devis.
    /// </summary>
    Task<IReadOnlyList<DeliveryZone>> ListActiveAsync(CancellationToken cancellationToken = default);

    Task<DeliveryZone?> GetByIdAsync(DeliveryZoneId id, CancellationToken cancellationToken = default);

    Task AddAsync(DeliveryZone zone, CancellationToken cancellationToken = default);
}
