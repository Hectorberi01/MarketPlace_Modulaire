using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HbaExpress.Dispatch.Domain.Couriers;

/// <summary>
/// Un point sur la carte, et la seule opération dont le dispatch a besoin : la distance
/// entre deux points.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// FORMULE DE HAVERSINE, PAS PostGIS.
///
/// Le matching demande « quels coursiers sont à moins de 3 km du premier ramassage ».
/// À cette échelle — une ville, quelques kilomètres — la distance à vol d'oiseau sur une
/// sphère est exacte à quelques mètres près, et se calcule en une ligne sans extension
/// de base de données ni dépendance.
///
/// CE QU'ELLE NE DIT PAS, et il faut le savoir : c'est un vol d'oiseau, pas un trajet.
/// À Cotonou, la lagune peut mettre trois kilomètres de route entre deux points distants
/// de huit cents mètres. Le rayon sert donc à PRÉSÉLECTIONNER des candidats, jamais à
/// promettre un délai. Un vrai temps de parcours demanderait un moteur d'itinéraire —
/// c'est une autre brique, et elle n'est pas nécessaire pour offrir une course.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class GeoPoint : ValueObject
{
    /// <summary>Rayon moyen de la Terre, en kilomètres.</summary>
    private const double EarthRadiusKm = 6371.0088;

    private GeoPoint(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }
    public double Longitude { get; }

    public static Result<GeoPoint> Create(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
        {
            return Error.Validation("courier.geo.latitude_invalid", "Latitude hors bornes.");
        }

        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
        {
            return Error.Validation("courier.geo.longitude_invalid", "Longitude hors bornes.");
        }

        // Le point (0, 0) est au large du golfe de Guinée — à quelques centaines de
        // kilomètres du Bénin. C'est la valeur qu'on obtient quand un GPS répond
        // « pas de fix » et que le client envoie ses champs à zéro. L'accepter placerait
        // des coursiers en pleine mer, et le rayon de dispatch ne trouverait jamais
        // personne sans qu'on comprenne pourquoi.
        if (latitude == 0d && longitude == 0d)
        {
            return Error.Validation("courier.geo.null_island", "Position (0, 0) refusée : GPS sans fix.");
        }

        return new GeoPoint(latitude, longitude);
    }

    /// <summary>Distance à vol d'oiseau, en kilomètres.</summary>
    public double DistanceKmTo(GeoPoint other)
    {
        var lat1 = ToRadians(Latitude);
        var lat2 = ToRadians(other.Latitude);
        var deltaLat = ToRadians(other.Latitude - Latitude);
        var deltaLon = ToRadians(other.Longitude - Longitude);

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Latitude;
        yield return Longitude;
    }
}
