using MediatR;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Application.Orders.Commands;
using Marketplace.Modules.Payments.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Ordering.Application.Orders.EventHandlers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// SUITE DU SAGA CÔTÉ COMMANDE — ET LE POINT OÙ LES ÉCHECS DISPARAISSAIENT.
///
/// Ces deux handlers renvoyaient directement le `Task&lt;Result&gt;` de `ISender.Send`
/// sous une signature `Task`. Le `Result` était donc jeté, et rien en aval ne le
/// rattrapait : `IntegrationEventDispatcher` n'inspecte pas ce que rend un handler, et
/// `OutboxProcessor` ne considère un message comme non traité que sur EXCEPTION.
///
/// Un `Result.Failure` était donc, pour l'outbox, un succès. Ni rejeu, ni backoff, ni
/// lettre morte, ni métrique — tout le mécanisme prévu pour cela était contourné par
/// le simple fait de renvoyer un échec typé au lieu de lever.
///
/// LE SCÉNARIO, ET IL EST ATTEIGNABLE. L'acheteur annule pendant que son paiement est
/// en vol — `Order.Cancel` l'autorise depuis « en attente de paiement », et la route
/// est publique. Ou bien le balayeur d'abandon annule la commande à six heures et le
/// paiement Mobile Money aboutit quelques minutes plus tard. Dans les deux cas
/// `MarkPaid` refuse, l'échec s'évapore, et le message est classé traité : acheteur
/// débité, commande annulée, aucun remboursement, aucune alerte.
///
/// ON LÈVE DONC. C'est le seul langage que l'outbox comprend, et il est déjà outillé
/// pour : dix tentatives avec backoff et gigue, puis lettre morte journalisée en
/// Critical avec métrique dédiée. Les rejeux légitimes, eux, ne lèvent plus — les
/// handlers de commande portent désormais leur garde d'idempotence.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ConfirmOrderOnPaymentCapturedHandler : IIntegrationEventHandler<PaymentCapturedIntegrationEvent>
{
    private readonly ISender _sender;

    public ConfirmOrderOnPaymentCapturedHandler(ISender sender) => _sender = sender;

    public async Task HandleAsync(PaymentCapturedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new ConfirmOrderPaymentCommand(integrationEvent.OrderId), cancellationToken);

        IntegrationHandlerFailure.ThrowIfFailed(
            result,
            $"Confirmation de la commande {integrationEvent.OrderId} après capture du paiement");
    }
}

/// <summary>
/// À l'échec du paiement, annule la commande et libère les réservations.
///
/// Même remontée d'erreur que ci-dessus : une annulation qui échoue laisserait du
/// stock réservé et de la cagnotte engagée sur une commande que personne ne paiera.
/// </summary>
public sealed class CancelOrderOnPaymentFailedHandler : IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    private readonly ISender _sender;

    public CancelOrderOnPaymentFailedHandler(ISender sender) => _sender = sender;

    public async Task HandleAsync(PaymentFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new CancelOrderCommand(integrationEvent.OrderId, $"Paiement échoué : {integrationEvent.Reason}"),
            cancellationToken);

        IntegrationHandlerFailure.ThrowIfFailed(
            result,
            $"Annulation de la commande {integrationEvent.OrderId} après échec du paiement");
    }
}
