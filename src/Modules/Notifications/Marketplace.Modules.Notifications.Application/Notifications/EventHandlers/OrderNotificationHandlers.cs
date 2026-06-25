using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;

/// <summary>Notifie l'acheteur que sa commande est passée (en attente de paiement).</summary>
public sealed class OrderPlacedNotificationHandler : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public OrderPlacedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(OrderPlacedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.BuyerId, "Commande enregistrée",
            $"Votre commande est enregistrée pour {e.GrandTotal:0.00} {e.Currency}. En attente de paiement.",
            "Order", e.OrderId, cancellationToken);
}

/// <summary>Notifie l'acheteur que sa commande est confirmée.</summary>
public sealed class OrderConfirmedNotificationHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public OrderConfirmedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(OrderConfirmedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.BuyerId, "Commande confirmée",
            "Votre paiement a été reçu et votre commande est confirmée. Préparation en cours.",
            "Order", e.OrderId, cancellationToken);
}

/// <summary>Notifie l'acheteur que sa commande est annulée.</summary>
public sealed class OrderCancelledNotificationHandler : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public OrderCancelledNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(OrderCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.BuyerId, "Commande annulée",
            $"Votre commande a été annulée. Motif : {e.Reason}",
            "Order", e.OrderId, cancellationToken);
}
