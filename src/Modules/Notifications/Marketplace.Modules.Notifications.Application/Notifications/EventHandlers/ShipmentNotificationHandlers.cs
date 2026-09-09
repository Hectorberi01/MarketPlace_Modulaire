using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Sellers.Contracts;
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

/// <summary>
/// Prévient le VENDEUR qu'aucun coursier n'a emporté son colis, et lui dit quoi faire.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// SANS CE HANDLER, TOUTE LA CORRECTION S'ARRÊTE À UNE LIGNE EN BASE.
///
/// L'échec d'une course ne se voyait nulle part côté marketplace. Il se voit maintenant
/// sur l'expédition et dans le journal — mais le colis est chez le VENDEUR, et c'est lui,
/// et lui seul, qui peut le faire repartir : depuis « en préparation », il confie la part
/// à un transporteur tiers (`MarkShipped`, repli du CDC §5.7). Un incident que la seule
/// personne capable d'agir n'apprend pas est un incident non corrigé.
///
/// PAS L'ACHETEUR, DÉLIBÉRÉMENT. Il ne peut rien faire de cette information, et le
/// vendeur réexpédie souvent dans l'heure. Lui annoncer un échec avant de savoir s'il a
/// une suite produit un appel au support et une inquiétude, pas une résolution. Il sera
/// prévenu par le chemin normal — « colis expédié » — quand le colis repartira.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// UNE SEULE ALERTE PAR COURSE ÉCHOUÉE, MÊME SI L'ÉVÉNEMENT EST REJOUÉ.
///
/// Deux rejeux se superposent ici : celui du drain du service coursier, qui réessaie son
/// appel HTTP jusqu'à dix fois quand la réponse se perd, et celui de l'outbox, qui
/// redistribue le message d'intégration autant de fois si un consommateur voisin échoue.
/// Le premier est fermé en amont, sur l'expédition (`FailedDeliveryJobId`) ; le second ne
/// peut l'être QU'ICI, parce que l'outbox rejoue APRÈS la transaction qui a écrit
/// l'annotation. D'où `NotifyOnceAsync` : le vendeur reçoit une mauvaise nouvelle, pas
/// dix.
///
/// La clé porte la COURSE, pas la commande : deux courses successives qui échouent sur
/// la même commande sont deux faits distincts. Voir NotificationDeduplicationKeys.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class CourierDeliveryFailedSellerNotificationHandler
    : IIntegrationEventHandler<CourierDeliveryFailedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<CourierDeliveryFailedSellerNotificationHandler> _logger;

    public CourierDeliveryFailedSellerNotificationHandler(
        NotificationDispatcher dispatcher,
        ISellerModuleApi sellers,
        ILogger<CourierDeliveryFailedSellerNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(
        CourierDeliveryFailedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // TRADUCTION SellerId → UserId. Une expédition connaît une BOUTIQUE ; un push et
        // un e-mail s'adressent à un COMPTE. Notifier l'identifiant de boutique
        // enverrait le message à un destinataire qui ne possède aucun appareil, sans la
        // moindre erreur.
        var seller = await _sellers.GetSellerAsync(e.SellerId, cancellationToken);
        if (seller is null)
        {
            _logger.LogError(
                "Course {DeliveryJobId} en échec sur la commande {OrderId} : vendeur {SellerId} "
                + "INTROUVABLE — il ne saura pas que son colis est resté en boutique.",
                e.DeliveryJobId, e.OrderId, e.SellerId);
            return;
        }

        // alsoEmail : le vendeur n'ouvrira pas forcément l'application, et l'action
        // attendue de lui est la seule chose qui débloque une commande déjà payée.
        await _dispatcher.NotifyOnceAsync(
            NotificationDeduplicationKeys.CourierDeliveryFailedSeller(e.DeliveryJobId, seller.UserId),
            seller.UserId,
            "Colis non enlevé : action requise",
            $"Aucun coursier n'a pu enlever votre colis. Motif : {e.Reason} "
            + "La commande reste payée et votre part est toujours « en préparation » : "
            + "expédiez-la par un transporteur depuis votre tableau de bord, ou contactez le support.",
            "Shipment",
            e.ShipmentId,
            cancellationToken,
            alsoEmail: true);
    }
}
