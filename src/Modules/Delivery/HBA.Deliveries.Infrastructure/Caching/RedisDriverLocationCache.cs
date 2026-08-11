using System.Globalization;
using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HBA.Deliveries.Infrastructure.Caching;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// POSITIONS DES LIVREURS — REDIS, ET SES COMMANDES GÉOSPATIALES.
///
/// Redis sait répondre nativement à « qui est dans ce rayon » : les positions
/// vivent dans un index géospatial (GEOADD), interrogé par GEOSEARCH. C'est la
/// raison principale du choix — sans cela, il faudrait charger tous les livreurs
/// en ligne et calculer les distances en mémoire à chaque course.
///
/// DEUX STRUCTURES, PARCE QU'UN INDEX GÉO NE SAIT PAS EXPIRER
///
/// Redis n'applique pas de durée de vie aux MEMBRES d'un index géospatial : on
/// ne peut faire expirer que la clé entière. Or une position doit se périmer
/// individuellement — un livreur dont le téléphone n'émet plus depuis deux
/// minutes ne doit plus être proposé, sans effacer tous les autres.
///
/// D'où une seconde clé par livreur, celle-ci avec un TTL. L'index géo répond
/// « qui est à proximité », et la clé horodatée dit « lesquels sont encore
/// frais ». Un livreur absent de la seconde est ignoré, et son entrée dans
/// l'index sera écrasée à sa prochaine émission ou nettoyée par le passage
/// suivant.
///
/// Ce n'est pas élégant. C'est la contrepartie assumée d'une structure qui, en
/// échange, répond en une milliseconde à la seule question que pose le dispatch.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class RedisDriverLocationCache : IDriverLocationCache
{
    /// <summary>Index géospatial de tous les livreurs en ligne.</summary>
    private const string GeoKey = "hba:drivers:geo";

    /// <summary>Préfixe des clés horodatées, porteuses du TTL.</summary>
    private const string SeenPrefix = "hba:drivers:seen:";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisDriverLocationCache> _logger;

    public RedisDriverLocationCache(IConnectionMultiplexer redis, ILogger<RedisDriverLocationCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private IDatabase Db => _redis.GetDatabase();

    public async Task SetAsync(DriverId driverId, Coordinates position, CancellationToken cancellationToken = default)
    {
        var member = driverId.Value.ToString();

        // GEOADD attend (longitude, latitude) — dans cet ordre. L'inverser est
        // l'erreur classique de tout code géospatial, et elle est INDÉTECTABLE au
        // Bénin : latitude ~6,4 et longitude ~2,4 sont deux nombres à un chiffre
        // parfaitement plausibles l'un pour l'autre. Le point atterrit simplement
        // ailleurs, sans qu'aucune validation ne se déclenche.
        await Db.GeoAddAsync(GeoKey, position.Longitude, position.Latitude, member);

        await Db.StringSetAsync(
            SeenPrefix + member,
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            DriverLocation.MaxPositionAge);
    }

    public async Task RemoveAsync(DriverId driverId, CancellationToken cancellationToken = default)
    {
        var member = driverId.Value.ToString();
        await Db.GeoRemoveAsync(GeoKey, member);
        await Db.KeyDeleteAsync(SeenPrefix + member);
    }

    public async Task<DriverPosition?> GetAsync(DriverId driverId, CancellationToken cancellationToken = default)
    {
        var member = driverId.Value.ToString();

        var seen = await Db.StringGetAsync(SeenPrefix + member);
        if (seen.IsNullOrEmpty)
        {
            return null; // position périmée : la clé horodatée a expiré
        }

        var positions = await Db.GeoPositionAsync(GeoKey, new RedisValue[] { member });
        if (positions.Length == 0 || positions[0] is not { } geo)
        {
            return null;
        }

        var coordinates = Coordinates.Create(geo.Latitude, geo.Longitude);
        if (coordinates.IsFailure)
        {
            return null;
        }

        return new DriverPosition(driverId, coordinates.Value, ParseSeen(seen!));
    }

    public async Task<IReadOnlyList<DriverPosition>> FindNearbyAsync(
        Coordinates center,
        double radiusKm,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        GeoRadiusResult[] results;
        try
        {
            results = await Db.GeoSearchAsync(
                GeoKey,
                center.Longitude,
                center.Latitude,
                new GeoSearchCircle(radiusKm, GeoUnit.Kilometers),
                count: limit * 4,
                options: GeoRadiusOptions.WithCoordinates);
        }
        catch (RedisServerException ex)
        {
            // GEOSEARCH exige Redis 6.2+. Sur une version antérieure, mieux vaut
            // un dispatch dégradé qu'un dispatch arrêté : on retombe sur l'ordre
            // de l'index, sans notion de distance.
            _logger.LogWarning(ex, "GEOSEARCH indisponible ; recherche de livreurs dégradée (sans distance).");
            var all = await Db.SortedSetRangeByRankAsync(GeoKey, 0, limit * 4);
            return await FilterFreshAsync(all.Select(m => (m, (double?)null)), limit);
        }

        var fresh = new List<DriverPosition>(results.Length);

        foreach (var result in results)
        {
            if (result.Position is not { } geo)
            {
                continue;
            }

            var seen = await Db.StringGetAsync(SeenPrefix + result.Member);
            if (seen.IsNullOrEmpty)
            {
                continue; // périmée
            }

            if (!Guid.TryParse(result.Member.ToString(), out var id))
            {
                continue;
            }

            var coordinates = Coordinates.Create(geo.Latitude, geo.Longitude);
            if (coordinates.IsFailure)
            {
                continue;
            }

            fresh.Add(new DriverPosition(new DriverId(id), coordinates.Value, ParseSeen(seen!)));

            if (fresh.Count >= limit)
            {
                break;
            }
        }

        return fresh;
    }

    /// <summary>Ne conserve que les livreurs dont la position est encore fraîche.</summary>
    private async Task<IReadOnlyList<DriverPosition>> FilterFreshAsync(
        IEnumerable<(RedisValue Member, double? _)> members, int limit)
    {
        var fresh = new List<DriverPosition>();

        foreach (var (member, _) in members)
        {
            if (fresh.Count >= limit)
            {
                break;
            }

            var seen = await Db.StringGetAsync(SeenPrefix + member);
            if (seen.IsNullOrEmpty || !Guid.TryParse(member.ToString(), out var id))
            {
                continue;
            }

            var positions = await Db.GeoPositionAsync(GeoKey, new RedisValue[] { member });
            if (positions.Length == 0 || positions[0] is not { } geo)
            {
                continue;
            }

            var coordinates = Coordinates.Create(geo.Latitude, geo.Longitude);
            if (coordinates.IsFailure)
            {
                continue;
            }

            fresh.Add(new DriverPosition(new DriverId(id), coordinates.Value, ParseSeen(seen!)));
        }

        return fresh;
    }

    private static DateTime ParseSeen(string value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.UtcNow;
}
