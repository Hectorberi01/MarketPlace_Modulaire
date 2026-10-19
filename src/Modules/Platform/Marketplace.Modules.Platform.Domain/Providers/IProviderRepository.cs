namespace Marketplace.Modules.Platform.Domain.Providers;

public interface IProviderRepository
{
    Task<Provider?> GetAsync(ProviderId id, CancellationToken cancellationToken = default);

    Task<Provider?> FindAsync(ProviderChannel channel, string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Provider>> ListAsync(ProviderChannel? channel = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fournisseurs actifs d'un canal, par priorité croissante. C'est la lecture du
    /// chemin chaud (chaque paiement, chaque e-mail) : elle est mise en cache par le
    /// registre, pas ici.
    /// </summary>
    Task<IReadOnlyList<Provider>> ListEnabledAsync(ProviderChannel channel, CancellationToken cancellationToken = default);

    void Add(Provider provider);

    void Remove(Provider provider);

    void AppendLog(ProviderChangeLog entry);

    Task<IReadOnlyList<ProviderChangeLog>> ListLogAsync(int limit, CancellationToken cancellationToken = default);
}
