using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Disputes.Contracts.IntegrationEvents;
using Marketplace.Modules.Disputes.Domain.Disputes.Events;

namespace Marketplace.Modules.Disputes.Application.Disputes.EventHandlers;

/// <summary>Publie « litige ouvert » (Settlement/Payments gèlent l'escrow, Notifications informe).</summary>
public sealed class DisputeOpenedDomainEventHandler : IDomainEventHandler<DisputeOpenedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DisputeOpenedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DisputeOpenedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DisputeOpenedIntegrationEvent
            {
                DisputeId = domainEvent.DisputeId,
                OrderId = domainEvent.OrderId,
                RaisedBy = domainEvent.RaisedBy,
                Type = domainEvent.Type
            },
            cancellationToken);
}

/// <summary>Publie « litige tranché » (Settlement/Payments libèrent l'escrow, Notifications informe).</summary>
public sealed class DisputeResolvedDomainEventHandler : IDomainEventHandler<DisputeResolvedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DisputeResolvedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DisputeResolvedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DisputeResolvedIntegrationEvent
            {
                DisputeId = domainEvent.DisputeId,
                OrderId = domainEvent.OrderId,
                Resolution = domainEvent.Resolution
            },
            cancellationToken);
}
