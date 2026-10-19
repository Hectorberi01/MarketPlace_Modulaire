using Marketplace.Modules.Platform.Domain.Providers;

namespace Marketplace.Modules.Platform.Application.Abstractions;

/// <summary>
/// Purge le cache du registre après une écriture.
///
/// Le format des clés de cache appartient au registre, en infrastructure : les
/// handlers n'ont donc pas à le connaître, ils disent seulement quel canal a bougé.
/// Sans cet appel, un fournisseur activé depuis la console resterait ignoré jusqu'à
/// expiration du cache — un réglage qui « ne prend pas », sans erreur.
/// </summary>
public interface IProviderCacheInvalidator
{
    Task InvalidateAsync(ProviderChannel channel, CancellationToken cancellationToken = default);
}
