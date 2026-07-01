using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marketplace.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Enregistrement conditionnel du processeur d'outbox. Les modules sont composés
/// in-process par PLUSIEURS hôtes (API + 4 BFF) : si chacun lançait l'outbox de
/// tous les modules, on multiplierait les connexions Postgres (≈ modules × hôtes)
/// jusqu'à épuisement (« too many clients »). On ne lance donc l'outbox que là où
/// la variable d'environnement OUTBOX_ENABLED n'est pas « false » — typiquement
/// l'API uniquement ; les BFF la positionnent à « false ».
/// </summary>
public static class OutboxRegistration
{
    public static bool Enabled =>
        !string.Equals(Environment.GetEnvironmentVariable("OUTBOX_ENABLED"), "false", StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddOutboxProcessor<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext, IOutboxDbContext
    {
        if (Enabled)
        {
            services.AddHostedService<OutboxProcessor<TDbContext>>();
        }

        return services;
    }
}
