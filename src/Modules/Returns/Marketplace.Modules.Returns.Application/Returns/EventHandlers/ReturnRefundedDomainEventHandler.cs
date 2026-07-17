using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Returns.Contracts.IntegrationEvents;
using Marketplace.Modules.Returns.Domain.Returns.Events;

namespace Marketplace.Modules.Returns.Application.Returns.EventHandlers;

/// <summary>
/// Publie « remboursement validé » — l'argent n'est pas encore parti.
///
/// Notifications s'en sert pour rassurer l'acheteur pendant l'attente. Rien d'autre
/// ne doit s'y accrocher : aucune écriture comptable ne peut découler d'une simple
/// décision.
/// </summary>
public sealed class ReturnRefundApprovedDomainEventHandler : IDomainEventHandler<ReturnRefundApprovedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ReturnRefundApprovedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(ReturnRefundApprovedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ReturnRefundApprovedIntegrationEvent
            {
                ReturnRequestId = domainEvent.ReturnRequestId,
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                SellerId = domainEvent.SellerId,
                RefundAmount = domainEvent.RefundAmount,
                Currency = domainEvent.Currency,
            },
            cancellationToken);
}

/// <summary>
/// Publie « argent versé » (Settlement contre-passe, Notifications informe).
///
/// N'est émis que sur confirmation d'exécution, référence FedaPay à l'appui.
/// </summary>
public sealed class ReturnRefundedDomainEventHandler : IDomainEventHandler<ReturnRefundedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ReturnRefundedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(ReturnRefundedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ReturnRefundedIntegrationEvent
            {
                ReturnRequestId = domainEvent.ReturnRequestId,
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                SellerId = domainEvent.SellerId,
                RefundAmount = domainEvent.RefundAmount,
                Currency = domainEvent.Currency,
                RefundReference = domainEvent.RefundReference,
            },
            cancellationToken);
}
