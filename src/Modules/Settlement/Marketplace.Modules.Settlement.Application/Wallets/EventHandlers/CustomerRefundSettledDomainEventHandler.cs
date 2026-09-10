using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Settlement.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Domain.Wallets.Events;

namespace Marketplace.Modules.Settlement.Application.Wallets.EventHandlers;

/// <summary>
/// Publie « remboursement client abouti ».
///
/// Ce flux était le SEUL des trois voies de remboursement de la place à être muet sur
/// le bus — et le seul qui frappe systématiquement une commande déjà confirmée, donc
/// déjà comptée dans le chiffre d'affaires. Analytics ne pouvait pas le déduire faute
/// de quoi que ce soit à écouter.
/// </summary>
public sealed class CustomerRefundSettledDomainEventHandler : IDomainEventHandler<CustomerRefundSettledDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public CustomerRefundSettledDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(CustomerRefundSettledDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new CustomerRefundSettledIntegrationEvent
            {
                CustomerRefundId = domainEvent.CustomerRefundId,
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                Amount = domainEvent.Amount,
                Currency = domainEvent.Currency,
                Channel = domainEvent.Channel
            },
            cancellationToken);
}
