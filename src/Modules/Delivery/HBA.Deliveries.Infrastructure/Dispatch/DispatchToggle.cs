using Marketplace.BuildingBlocks.Infrastructure.Outbox;

namespace HBA.Deliveries.Infrastructure.Dispatch;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// QUEL PROCESSUS A LE DROIT DE DISPATCHER.
///
/// Un seul, et pour la même raison que l'outbox : deux boucles concurrentes
/// liraient les mêmes courses et les proposeraient à deux livreurs différents.
///
/// PAR DÉFAUT, ON S'ALIGNE SUR L'OUTBOX plutôt que d'inventer un second réglage
/// à tenir à jour. Les hôtes qui drainent l'outbox — l'API — dispatchent ; ceux
/// qui ne le font pas — les quatre BFF — ne dispatchent pas. Aucun fichier de
/// déploiement à modifier, et la règle reste vraie le jour où un cinquième hôte
/// apparaît.
///
/// <c>DISPATCH_ENABLED</c> permet de les découpler si le besoin se présente : par
/// exemple confier le dispatch à un processus dédié, en le coupant sur l'API.
/// Tant que personne ne le pose, il n'existe pas — ce qui est très bien.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DispatchToggle
{
    public static bool Enabled
    {
        get
        {
            var flag = Environment.GetEnvironmentVariable("DISPATCH_ENABLED");

            return string.IsNullOrWhiteSpace(flag)
                ? OutboxRegistration.Enabled
                : !string.Equals(flag, "false", StringComparison.OrdinalIgnoreCase);
        }
    }
}
