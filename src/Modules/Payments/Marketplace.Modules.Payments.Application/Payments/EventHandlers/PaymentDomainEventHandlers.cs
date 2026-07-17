using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Observability;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Payments.Contracts.IntegrationEvents;
using Marketplace.Modules.Payments.Domain.Payments.Events;

namespace Marketplace.Modules.Payments.Application.Payments.EventHandlers;

/// <summary>Publie « paiement encaissé » (Ordering confirme) + métrique de succès.</summary>
public sealed class PaymentCapturedDomainEventHandler : IDomainEventHandler<PaymentCapturedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IPaymentMetrics _metrics;

    public PaymentCapturedDomainEventHandler(IIntegrationEventPublisher publisher, IPaymentMetrics metrics)
    {
        _publisher = publisher;
        _metrics = metrics;
    }

    public async Task HandleAsync(PaymentCapturedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _metrics.Success(
            domainEvent.Provider,
            domainEvent.Method,
            domainEvent.Currency,
            MoneyUnits.ToMinorUnits(domainEvent.Amount, domainEvent.Currency));

        await _publisher.PublishAsync(
            new PaymentCapturedIntegrationEvent
            {
                PaymentId = domainEvent.PaymentId,
                OrderId = domainEvent.OrderId
            },
            cancellationToken);
    }
}

/// <summary>Publie « paiement échoué » (Ordering annule) + métrique d'échec.</summary>
public sealed class PaymentFailedDomainEventHandler : IDomainEventHandler<PaymentFailedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IPaymentMetrics _metrics;

    public PaymentFailedDomainEventHandler(IIntegrationEventPublisher publisher, IPaymentMetrics metrics)
    {
        _publisher = publisher;
        _metrics = metrics;
    }

    public async Task HandleAsync(PaymentFailedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _metrics.Failed(domainEvent.Provider, domainEvent.Method, domainEvent.Currency, domainEvent.Reason);

        await _publisher.PublishAsync(
            new PaymentFailedIntegrationEvent
            {
                PaymentId = domainEvent.PaymentId,
                OrderId = domainEvent.OrderId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
    }
}

/// <summary>Publie « paiement remboursé » + métrique de remboursement.</summary>
public sealed class PaymentRefundedDomainEventHandler : IDomainEventHandler<PaymentRefundedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IPaymentMetrics _metrics;

    public PaymentRefundedDomainEventHandler(IIntegrationEventPublisher publisher, IPaymentMetrics metrics)
    {
        _publisher = publisher;
        _metrics = metrics;
    }

    public async Task HandleAsync(PaymentRefundedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _metrics.Refund(domainEvent.Provider, domainEvent.Currency, MoneyUnits.ToMinorUnits(domainEvent.Amount, domainEvent.Currency));

        await _publisher.PublishAsync(
            new PaymentRefundedIntegrationEvent
            {
                PaymentId = domainEvent.PaymentId,
                OrderId = domainEvent.OrderId
            },
            cancellationToken);
    }
}
