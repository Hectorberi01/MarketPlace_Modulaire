using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Delivery.Contracts.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace Marketplace.Modules.Delivery.Infrastructure.Dispatch;

/// <summary>
/// LE CHAÎNON QUI MANQUAIT ENTRE LA MARKETPLACE ET LE SERVICE DE DISPATCH.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// AVANT CE FICHIER, « COMMANDE PRÊTE À LIVRER » DISPARAISSAIT SANS BRUIT.
///
/// L'événement partait bien dans l'outbox de Shipping. L'outbox le drainait, le
/// dispatchait en mémoire — vers ZÉRO gestionnaire — puis le marquait traité. Pas
/// d'erreur, pas de lettre morte, pas de journal : une commande prête à partir
/// s'évanouissait, et le service de dispatch tournait à vide en attendant des courses
/// qui n'arrivaient jamais.
///
/// C'est la forme de panne la plus coûteuse d'un système d'intégration : tout paraît
/// fonctionner. Le seul symptôme est un acheteur qui attend.
///
/// TROIS ANNONCES, PARCE QUE LE SERVICE NE SAIT RIEN SANS ELLES : d'où viennent les
/// courses, quand elles cessent d'exister, et qui a le droit de les prendre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SendOrderReadyToDispatchHandler
    : IIntegrationEventHandler<OrderReadyForDeliveryIntegrationEvent>
{
    private readonly IDispatchServiceClient _dispatch;
    private readonly ILogger<SendOrderReadyToDispatchHandler> _logger;

    public SendOrderReadyToDispatchHandler(
        IDispatchServiceClient dispatch, ILogger<SendOrderReadyToDispatchHandler> logger)
    {
        _dispatch = dispatch;
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderReadyForDeliveryIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // Une commande sans point de ramassage n'est pas une course : l'envoyer ferait
        // répondre 400 au service, donc réessayer cinq fois pour rien. On le dit fort et
        // on s'arrête — l'anomalie est en amont, dans l'assemblage de l'événement.
        if (integrationEvent.Pickups.Count == 0)
        {
            _logger.LogError(
                "Commande {OrderId} annoncée prête SANS point de ramassage : aucune course ne sera créée.",
                integrationEvent.OrderId);
            return;
        }

        var payload = new DispatchOrderReady(
            integrationEvent.OrderId,
            integrationEvent.ShippingFee,
            integrationEvent.Currency,
            integrationEvent.Pickups.Select(ToPickup).ToList(),
            ToDropoff(integrationEvent.Dropoff));

        // AUCUN try/catch. Une exception rend la main à l'outbox, qui réessaie puis met
        // en lettre morte — voir DispatchServiceClient.
        await _dispatch.SendOrderReadyAsync(payload, cancellationToken);
    }

    private static DispatchPickup ToPickup(DeliveryPickupPointContract pickup)
        => new(
            pickup.ShipmentId, pickup.SellerId, pickup.SellerName, pickup.SellerPhone,
            pickup.CommuneCode, pickup.Quartier, pickup.Landmark, pickup.Line,
            pickup.Latitude, pickup.Longitude,
            pickup.Parcels.Select(p => new DispatchParcel(p.Sku, p.Quantity)).ToList());

    private static DispatchDropoff ToDropoff(DeliveryDropoffContract dropoff)
        => new(
            dropoff.BuyerId, dropoff.Recipient, dropoff.Phone, dropoff.CommuneCode,
            dropoff.Quartier, dropoff.Landmark, dropoff.Line,
            dropoff.Latitude, dropoff.Longitude);
}

/// <summary>
/// Commande annulée : la course tombe avec elle.
///
/// SANS CETTE ANNONCE, un coursier roulerait vers une commande qui n'existe plus, et
/// resterait marqué « en course » pour toujours — donc exclu de toutes les offres
/// suivantes. Une annulation non transmise retire un livreur de la flotte, en silence.
///
/// Le service répond sans erreur si la commande n'a jamais produit de course (annulation
/// avant préparation) : c'est le cas courant, et il ne doit pas faire réessayer.
/// </summary>
public sealed class SendOrderCancelledToDispatchHandler
    : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    private readonly IDispatchServiceClient _dispatch;

    public SendOrderCancelledToDispatchHandler(IDispatchServiceClient dispatch) => _dispatch = dispatch;

    public Task HandleAsync(
        OrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => _dispatch.SendOrderCancelledAsync(
            new DispatchOrderCancelled(
                integrationEvent.OrderId,
                string.IsNullOrWhiteSpace(integrationEvent.Reason) ? "Commande annulée." : integrationEvent.Reason),
            cancellationToken);
}

/// <summary>
/// Coursier vérifié : il peut recevoir des courses.
///
/// C'EST PAR ICI QU'UN COURSIER ENTRE DANS LE SERVICE DE DISPATCH. Il n'y a pas
/// d'inscription séparée, et il ne peut pas s'y déclarer actif lui-même : seule la
/// marketplace sait si son dossier est vérifié. Sans cette annonce, un coursier
/// parfaitement en règle reste inconnu du dispatch et ne reçoit jamais rien.
/// </summary>
public sealed class SendCourierActivatedToDispatchHandler
    : IIntegrationEventHandler<CourierActivatedIntegrationEvent>
{
    private readonly IDispatchServiceClient _dispatch;

    public SendCourierActivatedToDispatchHandler(IDispatchServiceClient dispatch) => _dispatch = dispatch;

    public Task HandleAsync(
        CourierActivatedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => _dispatch.SendCourierStandingAsync(
            new DispatchCourierStanding(
                integrationEvent.CourierId,
                integrationEvent.UserId,
                string.IsNullOrWhiteSpace(integrationEvent.DisplayName) ? "Coursier" : integrationEvent.DisplayName,
                "Active"),
            cancellationToken);
}

/// <summary>
/// Coursier suspendu : le dispatch cesse de lui offrir des courses.
///
/// LE NOM N'EST PAS TRANSMIS : l'événement de suspension ne le porte pas. Ce n'est pas
/// gênant — le service ne s'en sert que pour créer un coursier qu'il ne connaît pas
/// encore, jamais pour renommer un coursier existant.
///
/// LA SUSPENSION N'EST PAS UNE CONFISCATION : elle lui retire les courses à venir, pas ce
/// qu'il a déjà gagné. Le retrait de son portefeuille reste possible — voir
/// WithdrawalPayoutResolver, qui ne contrôle délibérément pas le statut.
/// </summary>
public sealed class SendCourierSuspendedToDispatchHandler
    : IIntegrationEventHandler<CourierSuspendedIntegrationEvent>
{
    private readonly IDispatchServiceClient _dispatch;

    public SendCourierSuspendedToDispatchHandler(IDispatchServiceClient dispatch) => _dispatch = dispatch;

    public Task HandleAsync(
        CourierSuspendedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => _dispatch.SendCourierStandingAsync(
            new DispatchCourierStanding(
                integrationEvent.CourierId, integrationEvent.UserId, string.Empty, "Suspended"),
            cancellationToken);
}
