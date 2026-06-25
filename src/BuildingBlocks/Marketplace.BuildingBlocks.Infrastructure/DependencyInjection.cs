using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Caching;
using Marketplace.BuildingBlocks.Infrastructure.Events;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;

namespace Marketplace.BuildingBlocks.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Services transverses d'infrastructure partagés par tous les modules :
    /// dispatch des domain events, dispatch in-process des integration events,
    /// et la file scopée d'events d'intégration (drainée par le DbContext du
    /// module vers son outbox). À appeler une fois dans le Bootstrap.
    /// </summary>
    public static IServiceCollection AddBuildingBlocksInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IntegrationEventDispatcher>();

        // Une file scopée par requête ; le même objet sert de publisher (côté
        // handlers) et de source de drainage (côté DbContext du module).
        services.AddScoped<IntegrationEventQueue>();
        services.AddScoped<IIntegrationEventPublisher>(sp => sp.GetRequiredService<IntegrationEventQueue>());

        // Cache distribué : mémoire par défaut (toujours résolvable). Le Bootstrap
        // remplace l'IDistributedCache par Redis quand « Redis:ConnectionString »
        // est renseigné.
        services.AddDistributedMemoryCache();
        services.AddSingleton<ICacheService, DistributedCacheService>();

        return services;
    }
}
