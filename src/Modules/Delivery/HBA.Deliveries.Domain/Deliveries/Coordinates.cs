using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>
/// Position géographique. Les deux valeurs vont ENSEMBLE ou pas du tout : une
/// latitude sans longitude ne place rien, et la moitié d'un point est plus
/// dangereuse que pas de point du tout — elle a l'air d'une donnée.
/// </summary>
public sealed class Coordinates : ValueObject
{
    private Coordinates(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    // Requis par EF Core.
    private Coordinates()
    {
    }

    public double Latitude { get; private init; }

    public double Longitude { get; private init; }

    public static Result<Coordinates> Create(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || double.IsNaN(longitude)
            || double.IsInfinity(latitude) || double.IsInfinity(longitude))
        {
            return Result.Failure<Coordinates>(
                Error.Validation("delivery.coordinates.invalid", "Coordonnées illisibles."));
        }

        if (latitude is < -90 or > 90)
        {
            return Result.Failure<Coordinates>(
                Error.Validation("delivery.coordinates.latitude_out_of_range", "La latitude doit être comprise entre -90 et 90."));
        }

        if (longitude is < -180 or > 180)
        {
            return Result.Failure<Coordinates>(
                Error.Validation("delivery.coordinates.longitude_out_of_range", "La longitude doit être comprise entre -180 et 180."));
        }

        // ─────────────────────────────────────────────────────────────────────
        // LE POINT (0, 0) EST REFUSÉ.
        //
        // Il est valide au sens des bornes, et il se trouve dans le golfe de
        // Guinée, à environ 600 km au sud du Bénin. C'est aussi la valeur que
        // produit tout code qui oublie de renseigner un point : un `default`,
        // un parsing raté, un champ vide converti en zéro.
        //
        // À cette latitude, l'erreur est indétectable à l'œil sur une carte
        // dézoomée — le point tombe simplement « un peu au sud ». On préfère un
        // refus explicite à un livreur envoyé vers l'océan.
        // ─────────────────────────────────────────────────────────────────────
        if (latitude == 0 && longitude == 0)
        {
            return Result.Failure<Coordinates>(
                Error.Validation("delivery.coordinates.null_island", "Position (0, 0) refusée : c'est la valeur par défaut d'un champ non renseigné."));
        }

        return new Coordinates(latitude, longitude);
    }

    /// <summary>
    /// Distance à vol d'oiseau, en kilomètres (formule de haversine).
    ///
    /// ⚠️ C'est une distance À VOL D'OISEAU, pas une distance routière. Elle sert
    /// à classer des livreurs par proximité et à borner un rayon de recherche —
    /// deux usages où l'ordre compte plus que l'exactitude. Elle ne doit PAS
    /// servir à facturer une course : à Cotonou, la lagune et les sens uniques
    /// font que deux points distants d'un kilomètre peuvent en demander quatre.
    /// La tarification a besoin d'un vrai calcul d'itinéraire.
    /// </summary>
    public double DistanceKmTo(Coordinates other)
    {
        const double earthRadiusKm = 6371.0;

        var dLat = ToRadians(other.Latitude - Latitude);
        var dLon = ToRadians(other.Longitude - Longitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(Latitude)) * Math.Cos(ToRadians(other.Latitude))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Latitude;
        yield return Longitude;
    }

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Latitude:F6},{Longitude:F6}");
}
