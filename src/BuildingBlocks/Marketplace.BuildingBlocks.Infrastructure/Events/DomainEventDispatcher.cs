using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.BuildingBlocks.Infrastructure.Events;

/// <summary>
/// Dispatch des domain events après persistance : pour chaque event, résout les
/// <see cref="IDomainEventHandler{TDomainEvent}"/> du module et les exécute.
/// Reste in-process et synchrone — un domain event ne quitte jamais son module.
/// </summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public DomainEventDispatcher(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public async Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            var handlers = _serviceProvider.GetServices(handlerType);

            foreach (var handler in handlers)
            {
                if (handler is null)
                {
                    continue;
                }

                var method = handlerType.GetMethod(
                    nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
                await (Task)method.Invoke(handler, new object[] { domainEvent, cancellationToken })!;
            }
        }
    }
}
