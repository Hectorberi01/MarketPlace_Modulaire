using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.BuildingBlocks.Infrastructure.Caching;

/// <summary>
/// Cache inerte : ne mémorise rien, n'invalide rien, et exécute toujours la source.
///
/// Destiné aux <c>IDesignTimeDbContextFactory</c> (outils EF : migrations add,
/// database update). Ces outils construisent un DbContext À LA MAIN, sans conteneur
/// DI et sans Redis. Exiger un vrai cache là reviendrait à exiger un Redis joignable
/// pour générer une migration — une dépendance absurde entre un outil de schéma et
/// une infrastructure d'exécution.
///
/// Il n'y a d'ailleurs rien à invalider au design-time : le DbContext n'y sert qu'à
/// produire du DDL, sans jamais écrire une ligne de données.
///
/// À ne JAMAIS enregistrer dans le conteneur d'une application. Ce n'est pas un
/// mode « cache désactivé » : c'est un bouchon pour outillage. Pour désactiver le
/// cache à l'exécution, on garde ICacheService et on laisse l'IDistributedCache en
/// mémoire — le comportement reste correct, seul le partage entre instances est perdu.
/// </summary>
public sealed class NoOpCacheService : ICacheService
{
    public static readonly NoOpCacheService Instance = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        => Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? ttl = null,
        TimeSpan? missTtl = null,
        CancellationToken cancellationToken = default)
        where T : class
        => factory(cancellationToken);
}
