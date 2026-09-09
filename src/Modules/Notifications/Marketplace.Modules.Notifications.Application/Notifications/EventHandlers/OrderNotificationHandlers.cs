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
            "Order", e.OrderId, cancellationToken, alsoEmail: true);
}

/// <summary>
/// Notifie l'acheteur que sa commande est confirmée — UNE SEULE FOIS.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE HANDLER N'AVAIT AUCUNE GARDE D'IDEMPOTENCE. SIX DE SES HUIT VOISINS EN ONT UNE.
///
/// `OrderConfirmedIntegrationEvent` a huit consommateurs. Shipping vérifie qu'aucune
/// expédition n'existe pour la commande, Analytics tient un marqueur `ProcessedOrder`,
/// Loyalty demande `HasEarnedForOrder`, Sellers repose le total exact au lieu de
/// l'incrémenter — chacun sait que le dispatch est « au moins une fois ». Ici, rien :
/// `NotifyAsync` créait sans condition.
///
/// Et le rejeu n'était pas une hypothèse. Notifications est dispatché AVANT Settlement,
/// Loyalty et Analytics (`ModuleRegistry`) : une panne dans l'un de ces trois rejouait
/// le message ENTIER, donc repassait ici. La politique autorise dix tentatives, et
/// `alsoEmail: true` fait partir un e-mail à chaque tour. L'acheteur pouvait recevoir
/// dix fois « Votre paiement a été reçu » : notification, push et e-mail.
///
/// La garde est posée sur la CLÉ du fait, pas sur le contenu du message : le libellé
/// peut changer demain sans rouvrir le trou.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class OrderConfirmedNotificationHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public OrderConfirmedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(OrderConfirmedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyOnceAsync(
            NotificationDeduplicationKeys.OrderConfirmedBuyer(e.OrderId),
            e.BuyerId, "Commande confirmée",
            "Votre paiement a été reçu et votre commande est confirmée. Préparation en cours.",
            "Order", e.OrderId, cancellationToken, alsoEmail: true);
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
            "Order", e.OrderId, cancellationToken, alsoEmail: true);
}
