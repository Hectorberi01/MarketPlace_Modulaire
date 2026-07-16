namespace Marketplace.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Cache distribué applicatif (Redis en production, mémoire en repli). Sérialise
/// en JSON. Sert au cache-aside des lectures chaudes (panier, read models…) et
/// reste transparent au métier : si le cache est froid, on retombe sur la source.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache-aside en un appel : renvoie la valeur en cache, sinon exécute
    /// <paramref name="factory"/> (la lecture en base) et mémorise son résultat.
    /// </summary>
    ///
    /// <remarks>
    /// ⚠️ CETTE MÉTHODE MÉMORISE AUSSI LES ABSENCES (« cache négatif »), et ce n'est
    /// pas un détail.
    ///
    /// Les fiches produit sont ANONYMES. Sans cache négatif, il suffit de demander
    /// des identifiants au hasard — /mobile/products/{guid} — pour traverser le cache
    /// à tous les coups et frapper la base à chaque requête. Le cache devient alors
    /// inutile exactement au moment où l'on en a besoin. C'est une attaque connue
    /// (« cache penetration »), et elle ne demande qu'une boucle.
    ///
    /// Une absence est donc mémorisée elle aussi, avec un TTL court
    /// (<paramref name="missTtl"/>) : assez pour absorber une rafale, assez bref pour
    /// qu'un produit tout juste créé apparaisse presque aussitôt.
    ///
    /// Le résultat est enveloppé pour distinguer « absent du cache » de « connu comme
    /// inexistant » — deux états que <c>null</c> seul ne sait pas séparer.
    /// </remarks>
    Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? ttl = null,
        TimeSpan? missTtl = null,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>Supprime plusieurs clés (invalidation d'une écriture qui touche plusieurs vues).</summary>
    Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
}
