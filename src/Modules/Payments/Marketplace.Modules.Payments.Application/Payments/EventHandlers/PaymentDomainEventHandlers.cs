using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Payments.Contracts.IntegrationEvents;
using Marketplace.Modules.Payments.Domain.Payments.Events;

namespace Marketplace.Modules.Payments.Application.Payments.EventHandlers;

/// <summary>Publie « paiement encaissé » (Ordering confirme la commande).</summary>
public sealed class PaymentCapturedDomainEventHandler : IDomainEventHandler<PaymentCapturedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public PaymentCapturedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(PaymentCapturedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new PaymentCapturedIntegrationEvent
            {
                PaymentId = domainEvent.PaymentId,
                OrderId = domainEvent.OrderId
            },
            cancellationToken);
}

/// <summary>Publie « paiement échoué » (Ordering annule et libère le stock).</summary>
public sealed class PaymentFailedDomainEventHandler : IDomainEventHandler<PaymentFailedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public PaymentFailedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(PaymentFailedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new PaymentFailedIntegrationEvent
            {
                PaymentId = domainEvent.PaymentId,
                OrderId = domainEvent.OrderId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Publie « paiement remboursé » (Notifications / comptabilité).</summary>
public sealed class PaymentRefundedDomainEventHandler : IDomainEventHandler<PaymentRefundedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public PaymentRefundedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(PaymentRefundedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new PaymentRefundedIntegrationEvent
            {
                PaymentId = domainEvent.PaymentId,
                OrderId = domainEvent.OrderId
            },
            cancellationToken);
}
