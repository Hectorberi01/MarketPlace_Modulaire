using System.Collections.Concurrent;
using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;

namespace HBA.Deliveries.Infrastructure.Caching;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// REPLI DE DÉVELOPPEMENT UNIQUEMENT.
///
/// POURQUOI IL EXISTE MALGRÉ TOUT
///
/// L'installer refusait de démarrer sans Redis. Le raisonnement était bon — les
/// positions SONT le cache, et un repli silencieux masquerait la panne jusqu'en
/// production — mais la conséquence ne l'était pas : plus aucun développeur ne
/// pouvait lancer l'API sans faire tourner un Redis, y compris pour travailler
/// sur le catalogue ou les commandes, qui n'ont rien à voir avec les livraisons.
///
/// CE QUI REND CE REPLI ACCEPTABLE
///
/// Il n'est JAMAIS choisi implicitement hors développement : ailleurs, l'absence
/// de Redis reste une erreur de démarrage. Et il journalise bruyamment ce qu'il
/// est, à chaque démarrage.
///
/// CE QU'IL NE FAIT PAS, ET QU'IL NE DOIT PAS FAIRE
///
/// Il ne partage rien entre processus. Deux instances de l'API auraient chacune
/// leur flotte de livreurs, et le dispatch ne trouverait que ceux « de son
/// côté ». C'est précisément le défaut qu'on refuse en production — et c'est
/// pourquoi ce type est interne et non configurable.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class InMemoryDriverLocationCache : IDriverLocationCache
{
    private readonly ConcurrentDictionary<DriverId, DriverPosition> _positions = new();

    public Task SetAsync(DriverId driverId, Coordinates position, CancellationToken cancellationToken = default)
    {
        _positions[driverId] = new DriverPosition(driverId, position, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(DriverId driverId, CancellationToken cancellationToken = default)
    {
        _positions.TryRemove(driverId, out _);
        return Task.CompletedTask;
    }

    public Task<DriverPosition?> GetAsync(DriverId driverId, CancellationToken cancellationToken = default)
    {
        if (!_positions.TryGetValue(driverId, out var position) || IsStale(position))
        {
            return Task.FromResult<DriverPosition?>(null);
        }

        return Task.FromResult<DriverPosition?>(position);
    }

    public Task<IReadOnlyList<DriverPosition>> FindNearbyAsync(
        Coordinates center,
        double radiusKm,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var fresh = _positions.Values.Where(p => !IsStale(p));

        // Même règle de péremption que l'implémentation Redis : le repli doit se
        // comporter comme l'original, sinon il devient un second système à
        // comprendre.
        var results = fresh
            .Select(p => (Position: p, Distance: center.DistanceKmTo(p.Position)))
            .Where(x => x.Distance <= radiusKm)
            .OrderBy(x => x.Distance)
            .Take(limit)
            .Select(x => x.Position)
            .ToList();

        return Task.FromResult<IReadOnlyList<DriverPosition>>(results);
    }

    private static bool IsStale(DriverPosition position)
        => DateTime.UtcNow - position.ReportedAtUtc > DriverLocation.MaxPositionAge;
}
