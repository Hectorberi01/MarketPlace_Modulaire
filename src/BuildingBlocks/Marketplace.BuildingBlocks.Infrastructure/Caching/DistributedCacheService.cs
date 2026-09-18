using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
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
    private static readonly TimeSpan DefaultMissTtl = TimeSpan.FromSeconds(30);

    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedCacheService> _logger;

    public DistributedCacheService(IDistributedCache cache, ILogger<DistributedCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Enveloppe de sérialisation. Elle existe pour une seule raison : distinguer
    /// « la clé est absente du cache » de « le cache SAIT que la valeur n'existe
    /// pas ». Sans elle, les deux cas se confondent en <c>null</c> et la seconde
    /// situation ne peut pas être mémorisée.
    /// </summary>
    private sealed record Envelope<T>(T? Value);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var bytes = await GetBytesAsync(key, cancellationToken);
        return bytes is null or { Length: 0 }
            ? default
            : JsonSerializer.Deserialize<T>(bytes, SerializerOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl };
        await SetBytesAsync(key, bytes, options, cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => RemoveCoreAsync(key, cancellationToken);

    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            await RemoveCoreAsync(key, cancellationToken);
        }
    }

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? ttl = null,
        TimeSpan? missTtl = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var bytes = await GetBytesAsync(key, cancellationToken);

        if (bytes is { Length: > 0 })
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<Envelope<T>>(bytes, SerializerOptions);

                // Un envelope désérialisé, MÊME avec Value == null, est un SUCCÈS de
                // cache : c'est le cache négatif. On renvoie null sans toucher la base.
                if (envelope is not null)
                {
                    return envelope.Value;
                }
            }
            catch (JsonException ex)
            {
                // Entrée corrompue, ou écrite par une version antérieure du contrat.
                // On l'ignore et on relit la source : un cache illisible doit
                // redevenir un cache vide, jamais casser une lecture.
                _logger.LogWarning(ex, "Entrée de cache illisible (« {CacheKey} ») ; relecture depuis la base.", key);
            }
        }

        var value = await factory(cancellationToken);

        var options = new DistributedCacheEntryOptions
        {
            // Une ABSENCE expire vite : assez pour absorber une rafale, trop peu pour
            // qu'un produit tout juste publié reste invisible.
            AbsoluteExpirationRelativeToNow = value is null
                ? missTtl ?? DefaultMissTtl
                : ttl ?? DefaultTtl,
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(new Envelope<T>(value), SerializerOptions);
        await SetBytesAsync(key, payload, options, cancellationToken);

        return value;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Accès à Redis — TOUJOURS en échec OUVERT (fail-open).
    //
    // Un cache est un accélérateur, pas une source de vérité. Si Redis tombe, la
    // marketplace doit RALENTIR, pas s'arrêter : on retombe sur la base. Laisser
    // remonter l'exception transformerait une panne de cache en panne totale —
    // autrement dit, ajouter du cache aurait RÉDUIT la disponibilité.
    //
    // (À ne pas confondre avec la vérification Turnstile du site, qui doit, elle,
    // échouer FERMÉE. Un cache et un contrôle de sécurité n'ont pas le même mode
    // de défaillance : l'un dégrade, l'autre protège.)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<byte[]?> GetBytesAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await _cache.GetAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache indisponible en lecture (« {CacheKey} ») ; repli sur la base.", key);
            return null;
        }
    }

    private async Task SetBytesAsync(string key, byte[] bytes, DistributedCacheEntryOptions options, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetAsync(key, bytes, options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache indisponible en écriture (« {CacheKey} ») ; valeur non mémorisée.", key);
        }
    }

    private async Task RemoveCoreAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ici, l'échec ouvert a un COÛT : la clé survit et servira une valeur
            // périmée jusqu'à son TTL. C'est le compromis retenu — les TTL sont
            // courts, et refuser l'écriture métier parce que le cache est en panne
            // serait pire. On journalise en Error : ce n'est pas anodin.
            _logger.LogError(ex, "Échec d'invalidation de « {CacheKey} » ; valeur périmée jusqu'à expiration du TTL.", key);
        }
    }
}
