using MediatR;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Application.Orders.Commands;
using Marketplace.Modules.Payments.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Ordering.Application.Orders.EventHandlers;

/// <summary>
/// Suite du Saga côté commande : à la capture du paiement, confirme la commande
/// (solde le stock) ; à l'échec, l'annule (libère le stock). Ordering ne dépend
/// que des Contracts de Payments.
/// </summary>
public sealed class ConfirmOrderOnPaymentCapturedHandler : IIntegrationEventHandler<PaymentCapturedIntegrationEvent>
{
    private readonly ISender _sender;

    public ConfirmOrderOnPaymentCapturedHandler(ISender sender) => _sender = sender;

    public Task HandleAsync(PaymentCapturedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => _sender.Send(new ConfirmOrderPaymentCommand(integrationEvent.OrderId), cancellationToken);
}

/// <summary>À l'échec du paiement, annule la commande et libère les réservations.</summary>
public sealed class CancelOrderOnPaymentFailedHandler : IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    private readonly ISender _sender;

    public CancelOrderOnPaymentFailedHandler(ISender sender) => _sender = sender;

    public Task HandleAsync(PaymentFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => _sender.Send(new CancelOrderCommand(integrationEvent.OrderId, $"Paiement échoué : {integrationEvent.Reason}"), cancellationToken);
}
