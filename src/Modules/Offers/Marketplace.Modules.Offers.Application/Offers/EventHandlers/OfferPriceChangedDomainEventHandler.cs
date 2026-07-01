using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Offers.Contracts.IntegrationEvents;
using Marketplace.Modules.Offers.Domain.Offers.Events;

namespace Marketplace.Modules.Offers.Application.Offers.EventHandlers;

/// <summary>Publie l'IntegrationEvent « prix d'offre changé » (Search réindexe prix/devise).</summary>
public sealed class OfferPriceChangedDomainEventHandler : IDomainEventHandler<OfferPriceChangedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OfferPriceChangedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(OfferPriceChangedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new OfferPriceChangedIntegrationEvent
            {
                OfferId = domainEvent.OfferId,
                ProductId = domainEvent.ProductId,
                BasePriceAmount = domainEvent.Amount,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}
