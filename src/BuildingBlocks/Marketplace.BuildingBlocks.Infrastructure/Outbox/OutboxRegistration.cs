using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Marketplace.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Déclare qu'un DbContext porte une table d'outbox. Enregistré sur TOUS les hôtes, même
/// ceux qui ne drainent pas — c'est ce qui permet à la console d'administration (BFF Admin,
/// où <c>OUTBOX_ENABLED=false</c>) de LIRE les lettres mortes de tous les modules sans jamais
/// les traiter.
/// </summary>
public sealed record OutboxContextRegistration(Type DbContextType, string ModuleName);

/// <summary>
/// Enregistrement conditionnel du processeur d'outbox. Les modules sont composés
/// in-process par PLUSIEURS hôtes (API + 4 BFF) : si chacun lançait l'outbox de
/// tous les modules, on multiplierait les connexions Postgres (≈ modules × hôtes)
/// jusqu'à épuisement (« too many clients »).
///
/// <para>
/// Il y avait une seconde raison, plus grave, et elle a été LEVÉE : les 5 processeurs
/// liraient les <b>MÊMES</b> messages et les dispatcheraient <b>5 FOIS</b>, chaque gain
/// vendeur crédité cinq fois. La lecture réserve désormais son lot par
/// <c>SELECT … FOR UPDATE SKIP LOCKED</c> (voir <c>OutboxProcessor.ClaimBatchAsync</c>) :
/// les processeurs se partagent le travail au lieu de le dupliquer.
/// </para>
///
/// <para>
/// <b>La mise à l'échelle horizontale de l'API n'est donc plus interdite par l'outbox.</b>
/// Ce qui subsiste ici est une question de RESSOURCES, pas de correction : n'ouvrir des
/// connexions Postgres que là où l'on draine réellement. Les BFF n'ont rien à drainer et
/// gardent <c>OUTBOX_ENABLED=false</c>.
/// </para>
/// </summary>
public static class OutboxRegistration
{
    public static bool Enabled =>
        !string.Equals(Environment.GetEnvironmentVariable("OUTBOX_ENABLED"), "false", StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddOutboxProcessor<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext, IOutboxDbContext
    {
        // Politique de réessai PARTAGÉE par tous les modules : un seul endroit où régler le
        // plafond de tentatives et le backoff, plutôt que vingt-cinq.
        //
        // TryAddSingleton, et non AddSingleton : cette méthode est appelée UNE FOIS PAR
        // MODULE. AddSingleton empilerait 25 descripteurs identiques — sans dommage
        // fonctionnel (le dernier gagne), mais c'est un mensonge dans le conteneur, et le
        // jour où quelqu'un injectera IEnumerable<OutboxRetryPolicy> il en trouvera 25.
        services.TryAddSingleton<OutboxRetryPolicy>();

        // Le registre est peuplé sur TOUS les hôtes, indépendamment d'Enabled : le BFF Admin
        // ne draine pas l'outbox, mais doit pouvoir en lister — et rejouer — les lettres
        // mortes.
        var moduleName = typeof(TDbContext).Name.Replace("DbContext", string.Empty);
        services.AddSingleton(new OutboxContextRegistration(typeof(TDbContext), moduleName));

        if (Enabled)
        {
            services.AddHostedService<OutboxProcessor<TDbContext>>();
        }

        return services;
    }
}
