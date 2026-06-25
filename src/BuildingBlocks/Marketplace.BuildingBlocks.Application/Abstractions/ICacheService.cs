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
}
