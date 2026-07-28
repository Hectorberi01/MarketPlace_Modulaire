using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Notifie l'acheteur qu'un colis est expédié. L'event d'expédition ne porte pas
/// le destinataire : on le résout via Ordering (Contracts).
/// </summary>
public sealed class ShipmentShippedNotificationHandler : IIntegrationEventHandler<ShipmentShippedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IOrderingModuleApi _orderingModuleApi;

    public ShipmentShippedNotificationHandler(NotificationDispatcher dispatcher, IOrderingModuleApi orderingModuleApi)
    {
        _dispatcher = dispatcher;
        _orderingModuleApi = orderingModuleApi;
    }

    public async Task HandleAsync(ShipmentShippedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var order = await _orderingModuleApi.GetOrderAsync(e.OrderId, cancellationToken);
        if (order is null)
        {
            return;
        }

        await _dispatcher.NotifyAsync(
            order.BuyerId, "Colis expédié",
            $"Un colis de votre commande a été expédié via {e.Carrier}. Suivi : {e.TrackingNumber}.",
            "Shipment", e.ShipmentId, cancellationToken, alsoEmail: true);
    }
}

/// <summary>Notifie l'acheteur qu'un colis est livré (destinataire résolu via Ordering).</summary>
public sealed class ShipmentDeliveredNotificationHandler : IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IOrderingModuleApi _orderingModuleApi;

    public ShipmentDeliveredNotificationHandler(NotificationDispatcher dispatcher, IOrderingModuleApi orderingModuleApi)
    {
        _dispatcher = dispatcher;
        _orderingModuleApi = orderingModuleApi;
    }

    public async Task HandleAsync(ShipmentDeliveredIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var order = await _orderingModuleApi.GetOrderAsync(e.OrderId, cancellationToken);
        if (order is null)
        {
            return;
        }

        await _dispatcher.NotifyAsync(
            order.BuyerId, "Colis livré",
            "Un colis de votre commande a été livré. Vous pouvez maintenant laisser un avis.",
            "Shipment", e.ShipmentId, cancellationToken, alsoEmail: true);
    }
}
