using HBA.Deliveries.Domain.Deliveries;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Pricing;

/// <summary>Un sommet de polygone. Volontairement distinct de <see cref="Coordinates"/> : ici, pas de validation métier de position.</summary>
/// <param name="Latitude">Latitude en degrés.</param>
/// <param name="Longitude">Longitude en degrés.</param>
public readonly record struct GeoPoint(double Latitude, double Longitude);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN CONTOUR DE ZONE — ET SON TEST D'APPARTENANCE.
///
/// POURQUOI PAS POSTGIS
///
/// PostGIS ferait cela mieux, avec un index GiST et des requêtes natives. Mais
/// il faudrait l'installer sur la base de production, changer l'image Docker, et
/// versionner une extension de plus. Pour quelques dizaines de zones tenues en
/// mémoire, le calcul C# répond en microsecondes et ne coûte aucune migration
/// d'infrastructure.
///
/// Le jour où l'on comptera des milliers de zones, ou qu'on voudra interroger
/// « toutes les courses dans ce quartier », PostGIS deviendra le bon outil. Ce
/// jour-là, le contour est déjà stocké sous forme de liste de sommets : la
/// bascule est une migration de données, pas une réécriture.
///
/// L'ALGORITHME : LANCER DE RAYON
///
/// On tire une demi-droite horizontale depuis le point et l'on compte les
/// arêtes traversées. Nombre impair : dedans. Nombre pair : dehors. C'est exact
/// pour tout polygone simple, y compris concave — et un quartier l'est presque
/// toujours.
///
/// ⚠️ Le comportement sur une arête EXACTE n'est pas garanti. C'est sans
/// conséquence ici : deux zones voisines se recouvrent volontairement de
/// quelques mètres, et la priorité tranche.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class GeoPolygon : ValueObject
{
    /// <summary>Trois sommets au minimum : en dessous, ce n'est pas une surface.</summary>
    public const int MinimumVertices = 3;

    private readonly List<GeoPoint> _vertices;

    private GeoPolygon(List<GeoPoint> vertices) => _vertices = vertices;

    // Requis par EF Core.
    private GeoPolygon() => _vertices = new List<GeoPoint>();

    public IReadOnlyList<GeoPoint> Vertices => _vertices;

    public static Result<GeoPolygon> Create(IEnumerable<GeoPoint>? vertices)
    {
        var points = vertices?.ToList() ?? new List<GeoPoint>();

        if (points.Count < MinimumVertices)
        {
            return Result.Failure<GeoPolygon>(
                Error.Validation("pricing.zone.polygon_too_small",
                    $"Un contour de zone demande au moins {MinimumVertices} sommets."));
        }

        foreach (var point in points)
        {
            if (Math.Abs(point.Latitude) > 90 || Math.Abs(point.Longitude) > 180)
            {
                return Result.Failure<GeoPolygon>(
                    Error.Validation("pricing.zone.vertex_out_of_range", "Un sommet du contour est hors limites."));
            }
        }

        // Le contour est FERMÉ implicitement : on n'exige pas que le dernier
        // sommet répète le premier, et on le retire s'il est présent. Les outils
        // de tracé le produisent, GeoJSON l'impose, et le lancer de rayon
        // referme de toute façon la boucle.
        if (points.Count > MinimumVertices && points[0] == points[^1])
        {
            points.RemoveAt(points.Count - 1);
        }

        return new GeoPolygon(points);
    }

    /// <summary>Le point est-il à l'intérieur du contour ?</summary>
    public bool Contains(double latitude, double longitude)
    {
        var inside = false;

        for (int i = 0, j = _vertices.Count - 1; i < _vertices.Count; j = i++)
        {
            var pi = _vertices[i];
            var pj = _vertices[j];

            // L'arête coupe-t-elle la bande horizontale du point ? Et si oui, le
            // fait-elle à droite de celui-ci ?
            var straddles = (pi.Latitude > latitude) != (pj.Latitude > latitude);
            if (!straddles)
            {
                continue;
            }

            var crossingLongitude = pi.Longitude
                + (latitude - pi.Latitude) * (pj.Longitude - pi.Longitude) / (pj.Latitude - pi.Latitude);

            if (longitude < crossingLongitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public bool Contains(Coordinates coordinates) => Contains(coordinates.Latitude, coordinates.Longitude);

    /// <summary>
    /// Reconstruit un contour lu en base, SANS revalider.
    ///
    /// La validation a eu lieu à la création ; la rejouer à chaque lecture ferait
    /// échouer le chargement de zones parfaitement valides le jour où une règle
    /// se durcit — et le refus surviendrait au démarrage, sur des données qu'on
    /// ne peut plus corriger sans migration.
    /// </summary>
    public static GeoPolygon Rehydrate(IEnumerable<GeoPoint> vertices) => new(vertices.ToList());

    protected override IEnumerable<object?> GetAtomicValues()
    {
        foreach (var vertex in _vertices)
        {
            yield return vertex;
        }
    }
}
