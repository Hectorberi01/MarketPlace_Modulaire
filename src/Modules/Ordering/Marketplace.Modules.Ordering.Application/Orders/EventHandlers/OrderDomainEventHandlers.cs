using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Ordering.Domain.Orders.Events;

namespace Marketplace.Modules.Ordering.Application.Orders.EventHandlers;

/// <summary>Publie l'IntegrationEvent « commande placée » (Cart clôture, Payments démarre).</summary>
public sealed class OrderPlacedDomainEventHandler : IDomainEventHandler<OrderPlacedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OrderPlacedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(OrderPlacedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new OrderPlacedIntegrationEvent
            {
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                CartId = domainEvent.CartId,
                GrandTotal = domainEvent.GrandTotal,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}

/// <summary>Publie l'IntegrationEvent « commande confirmée » (Shipping, Notifications).</summary>
public sealed class OrderConfirmedDomainEventHandler : IDomainEventHandler<OrderConfirmedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OrderConfirmedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(OrderConfirmedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new OrderConfirmedIntegrationEvent { OrderId = domainEvent.OrderId, BuyerId = domainEvent.BuyerId },
            cancellationToken);
}

/// <summary>Publie l'IntegrationEvent « commande annulée » (Notifications, analytics).</summary>
public sealed class OrderCancelledDomainEventHandler : IDomainEventHandler<OrderCancelledDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OrderCancelledDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(OrderCancelledDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new OrderCancelledIntegrationEvent { OrderId = domainEvent.OrderId, BuyerId = domainEvent.BuyerId, Reason = domainEvent.Reason },
            cancellationToken);
}

/// <summary>Publie l'IntegrationEvent « commande livrée » (Payments libère l'escrow, Settlement paie).</summary>
public sealed class OrderDeliveredDomainEventHandler : IDomainEventHandler<OrderDeliveredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OrderDeliveredDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(OrderDeliveredDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new OrderDeliveredIntegrationEvent { OrderId = domainEvent.OrderId, BuyerId = domainEvent.BuyerId },
            cancellationToken);
}
