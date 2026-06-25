using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.BuildingBlocks.Infrastructure.Caching;

/// <summary>
/// Implémentation de <see cref="ICacheService"/> au-dessus d'<see cref="IDistributedCache"/>
/// (Redis ou mémoire selon la configuration du Bootstrap). Sérialisation JSON ;
/// TTL par défaut de 5 minutes.
/// </summary>
public sealed class DistributedCacheService : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly IDistributedCache _cache;

    public DistributedCacheService(IDistributedCache cache) => _cache = cache;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var bytes = await _cache.GetAsync(key, cancellationToken);
        return bytes is null or { Length: 0 }
            ? default
            : JsonSerializer.Deserialize<T>(bytes, SerializerOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl };
        await _cache.SetAsync(key, bytes, options, cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => _cache.RemoveAsync(key, cancellationToken);
}
