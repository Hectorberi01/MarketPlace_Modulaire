using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.BuildingBlocks.Infrastructure.Events;

/// <summary>
/// Dispatch in-process d'un event d'intégration vers tous ses handlers
/// enregistrés. C'est l'implémentation « bus en mémoire » d'aujourd'hui ;
/// demain, le même contrat est rempli par un consumer Kafka — sans toucher au
/// code métier des handlers.
/// </summary>
public sealed class IntegrationEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public IntegrationEventDispatcher(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public async Task DispatchAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(integrationEvent.GetType());
        var handlers = _serviceProvider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            if (handler is null)
            {
                continue;
            }

            var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!;
            await (Task)method.Invoke(handler, new object[] { integrationEvent, cancellationToken })!;
        }
    }
}
