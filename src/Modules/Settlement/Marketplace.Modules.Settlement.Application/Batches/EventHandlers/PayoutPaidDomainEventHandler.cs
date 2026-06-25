using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Settlement.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Domain.Batches.Events;

namespace Marketplace.Modules.Settlement.Application.Batches.EventHandlers;

/// <summary>Publie « reversement versé » (Notifications informe le vendeur, compta enregistre).</summary>
public sealed class PayoutPaidDomainEventHandler : IDomainEventHandler<PayoutPaidDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public PayoutPaidDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(PayoutPaidDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new PayoutPaidIntegrationEvent
            {
                BatchId = domainEvent.BatchId,
                PayoutId = domainEvent.PayoutId,
                SellerId = domainEvent.SellerId,
                NetAmount = domainEvent.NetAmount,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}
