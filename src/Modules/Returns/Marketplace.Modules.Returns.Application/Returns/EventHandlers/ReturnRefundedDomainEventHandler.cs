using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Returns.Contracts.IntegrationEvents;
using Marketplace.Modules.Returns.Domain.Returns.Events;

namespace Marketplace.Modules.Returns.Application.Returns.EventHandlers;

/// <summary>Publie « retour remboursé » (Settlement reprend le payout, Notifications informe).</summary>
public sealed class ReturnRefundedDomainEventHandler : IDomainEventHandler<ReturnRefundedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ReturnRefundedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(ReturnRefundedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ReturnRefundedIntegrationEvent
            {
                ReturnRequestId = domainEvent.ReturnRequestId,
                OrderId = domainEvent.OrderId,
                SellerId = domainEvent.SellerId,
                RefundAmount = domainEvent.RefundAmount,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}
