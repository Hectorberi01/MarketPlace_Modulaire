using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Delivery.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Le coursier est vérifié : il peut travailler et être payé.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// TROIS ÉVÉNEMENTS QUI PARTAIENT DANS LE VIDE.
///
/// Activation, suspension, rémunération créditée : les trois étaient publiés, et
/// personne ne les écoutait. Le coursier apprenait qu'il était vérifié en essayant, qu'il
/// était suspendu en n'ayant plus de courses, et qu'il avait été payé en consultant son
/// solde — s'il pensait à le faire.
///
/// C'est exactement la forme du bug qui a coûté le plus cher sur ce projet :
/// « ReturnRefunded » était publié depuis le début et n'avait AUCUN consommateur.
/// Un événement sans abonné ne fait pas de bruit en échouant ; il ne fait rien, et
/// ça ne se voit nulle part.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CourierActivatedNotificationHandler : IIntegrationEventHandler<CourierActivatedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public CourierActivatedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(CourierActivatedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.UserId,
            "Votre compte coursier est validé",
            "Votre vérification est terminée. Vous pouvez recevoir des courses, et votre rémunération " +
            "sera versée sur votre compte Mobile Money.",
            "Courier",
            e.CourierId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>
/// Le coursier est suspendu. Le message dit AUSSI que son solde reste le sien : c'est
/// la première question qu'il se posera, et ne pas y répondre garantit un appel au
/// support.
/// </summary>
public sealed class CourierSuspendedNotificationHandler : IIntegrationEventHandler<CourierSuspendedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IDeliveryModuleApi _couriers;
    private readonly ILogger<CourierSuspendedNotificationHandler> _logger;

    public CourierSuspendedNotificationHandler(
        NotificationDispatcher dispatcher,
        IDeliveryModuleApi couriers,
        ILogger<CourierSuspendedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _couriers = couriers;
        _logger = logger;
    }

    public async Task HandleAsync(CourierSuspendedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // L'événement ne porte pas le compte utilisateur — un coursier suspendu n'a pas
        // à voir son identifiant de connexion voyager dans tous les messages du bus.
        var courier = await _couriers.GetCourierAsync(e.CourierId, cancellationToken);
        if (courier is null)
        {
            _logger.LogError(
                "Coursier {CourierId} suspendu mais introuvable : il ne sera pas informé.", e.CourierId);
            return;
        }

        await _dispatcher.NotifyAsync(
            courier.UserId,
            "Votre compte coursier est suspendu",
            $"Motif : {e.Reason}. Vous ne recevrez plus de courses. Le solde déjà gagné reste le vôtre " +
            "et vous pouvez toujours en demander le versement.",
            "Courier",
            e.CourierId,
            cancellationToken,
            alsoEmail: true);
    }
}

/// <summary>
/// Une course a été payée. C'est LE message que le coursier attend — celui qui lui dit
/// que son travail de la journée est devenu de l'argent.
/// </summary>
public sealed class CourierEarningCreditedNotificationHandler
    : IIntegrationEventHandler<CourierEarningCreditedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IDeliveryModuleApi _couriers;
    private readonly ILogger<CourierEarningCreditedNotificationHandler> _logger;

    public CourierEarningCreditedNotificationHandler(
        NotificationDispatcher dispatcher,
        IDeliveryModuleApi couriers,
        ILogger<CourierEarningCreditedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _couriers = couriers;
        _logger = logger;
    }

    public async Task HandleAsync(
        CourierEarningCreditedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var courier = await _couriers.GetCourierAsync(e.CourierId, cancellationToken);
        if (courier is null)
        {
            _logger.LogError(
                "Course {DeliveryJobId} payée au coursier {CourierId}, introuvable : il ne sera pas informé.",
                e.DeliveryJobId, e.CourierId);
            return;
        }

        // Pas d'e-mail : une course livrée est un événement fréquent, et inonder la boîte
        // d'un coursier qui en fait quinze par jour finirait par lui faire ignorer les
        // messages qui comptent vraiment — une suspension, par exemple.
        await _dispatcher.NotifyAsync(
            courier.UserId,
            "Course payée",
            $"{e.Amount:0.00} {e.Currency} ont été crédités sur votre solde. " +
            "Vous pouvez en demander le versement à tout moment.",
            "Courier",
            e.CourierId,
            cancellationToken);
    }
}
