using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Platform.Application.Abstractions;
using Marketplace.Modules.Platform.Contracts;
using Marketplace.Modules.Platform.Domain.Providers;

namespace Marketplace.Modules.Platform.Infrastructure.Public;

/// <summary>
/// Lecture du registre, avec cache et déchiffrement.
///
/// C'EST ICI QUE LES SECRETS REDEVIENNENT LISIBLES, et nulle part ailleurs.
///
/// CE QUI EST MIS EN CACHE RESTE CHIFFRÉ, et ce détail est le cœur de la décision.
/// Le cache peut être Redis : y déposer les clés d'API en clair rendrait une
/// sauvegarde Redis aussi payante qu'un vol de la clé maître, et annulerait le
/// chiffrement en base. On met donc en cache la forme protégée — pas plus sensible
/// que la table — et on déchiffre à chaque lecture. AES-GCM sur quelques dizaines
/// d'octets coûte des microsecondes ; la requête SQL, elle, est bien ce qu'on évite.
///
/// LE CACHE EST COURT **ET** INVALIDÉ. Les deux, parce qu'ils couvrent deux pannes
/// différentes : l'invalidation rend un changement de la console immédiat ; le TTL
/// garantit qu'un oubli d'invalidation — ou une écriture faite par un autre
/// processus — se rattrape de lui-même en une minute au lieu de durer jusqu'au
/// redémarrage.
/// </summary>
internal sealed class PlatformProviderRegistry : IPlatformProviderRegistry, IProviderCacheInvalidator
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    private readonly IProviderRepository _repository;
    private readonly ISecretProtector _protector;
    private readonly ICacheService _cache;

    public PlatformProviderRegistry(
        IProviderRepository repository, ISecretProtector protector, ICacheService cache)
    {
        _repository = repository;
        _protector = protector;
        _cache = cache;
    }

    public async Task<ProviderConfiguration?> GetActiveAsync(
        PlatformChannel channel, CancellationToken cancellationToken = default)
    {
        var actifs = await ListEnabledAsync(channel, cancellationToken);
        return actifs.Count == 0 ? null : actifs[0];
    }

    public async Task<IReadOnlyList<ProviderConfiguration>> ListEnabledAsync(
        PlatformChannel channel, CancellationToken cancellationToken = default)
    {
        var interne = ToDomain(channel);

        var instantanes = await _cache.GetOrCreateAsync(
            CacheKey(interne),
            async ct =>
            {
                var fournisseurs = await _repository.ListEnabledAsync(interne, ct);
                return fournisseurs.Select(ToSnapshot).ToList();
            },
            Ttl,
            // NOMMÉ, et c'est obligatoire : la signature intercale `missTtl` (cache
            // négatif) entre le TTL et le jeton. En positionnel, le jeton atterrit
            // sur `missTtl`.
            cancellationToken: cancellationToken);

        if (instantanes is null || instantanes.Count == 0)
        {
            return Array.Empty<ProviderConfiguration>();
        }

        return instantanes.Select(Decrypt).ToList();
    }

    public async Task<ProviderConfiguration?> FindAsync(
        PlatformChannel channel, string code, CancellationToken cancellationToken = default)
    {
        var actifs = await ListEnabledAsync(channel, cancellationToken);
        return actifs.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public Task InvalidateAsync(ProviderChannel channel, CancellationToken cancellationToken = default)
        => _cache.RemoveAsync(CacheKey(channel), cancellationToken);

    private static string CacheKey(ProviderChannel channel) => $"platform:providers:enabled:{channel}";

    /// <summary>Forme mise en cache : les secrets y restent CHIFFRÉS.</summary>
    internal sealed record ProviderSnapshot(
        string Code,
        string DisplayName,
        PlatformChannel Channel,
        PlatformProviderMode Mode,
        int Priority,
        Dictionary<string, string> Settings,
        Dictionary<string, string> ProtectedSecrets);

    private static ProviderSnapshot ToSnapshot(Provider provider)
    {
        var descriptor = ProviderCatalog.Find(provider.Channel, provider.Code);

        return new ProviderSnapshot(
            provider.Code,
            descriptor?.DisplayName ?? provider.Code,
            ToContract(provider.Channel),
            provider.Mode == ProviderMode.Live ? PlatformProviderMode.Live : PlatformProviderMode.Sandbox,
            provider.Priority,
            new Dictionary<string, string>(provider.ReadSettings(), StringComparer.Ordinal),
            new Dictionary<string, string>(provider.ReadProtectedSecrets(), StringComparer.Ordinal));
    }

    private ProviderConfiguration Decrypt(ProviderSnapshot snapshot)
        => new(
            snapshot.Code,
            snapshot.DisplayName,
            snapshot.Channel,
            snapshot.Mode,
            snapshot.Priority,
            snapshot.Settings,
            snapshot.ProtectedSecrets.ToDictionary(
                kv => kv.Key, kv => _protector.Unprotect(kv.Value), StringComparer.Ordinal));

    // Les deux énumérations sont volontairement distinctes (voir PlatformContracts) :
    // le mapping vit donc ici, et un membre ajouté d'un côté casse la compilation de
    // l'autre — ce qui est le comportement voulu.
    private static ProviderChannel ToDomain(PlatformChannel channel) => channel switch
    {
        PlatformChannel.Payment => ProviderChannel.Payment,
        PlatformChannel.Email => ProviderChannel.Email,
        PlatformChannel.Sms => ProviderChannel.Sms,
        PlatformChannel.WhatsApp => ProviderChannel.WhatsApp,
        PlatformChannel.Push => ProviderChannel.Push,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Canal inconnu.")
    };

    private static PlatformChannel ToContract(ProviderChannel channel) => channel switch
    {
        ProviderChannel.Payment => PlatformChannel.Payment,
        ProviderChannel.Email => PlatformChannel.Email,
        ProviderChannel.Sms => PlatformChannel.Sms,
        ProviderChannel.WhatsApp => PlatformChannel.WhatsApp,
        ProviderChannel.Push => PlatformChannel.Push,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Canal inconnu.")
    };
}
