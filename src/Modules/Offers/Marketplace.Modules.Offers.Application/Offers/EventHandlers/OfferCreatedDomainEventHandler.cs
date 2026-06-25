using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Offers.Contracts.IntegrationEvents;
using Marketplace.Modules.Offers.Domain.Offers.Events;

namespace Marketplace.Modules.Offers.Application.Offers.EventHandlers;

/// <summary>Publie l'IntegrationEvent « offre créée » (consommé par Search / Pricing).</summary>
public sealed class OfferCreatedDomainEventHandler : IDomainEventHandler<OfferCreatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OfferCreatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(OfferCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new OfferCreatedIntegrationEvent
            {
                OfferId = domainEvent.OfferId,
                ProductId = domainEvent.ProductId,
                SellerId = domainEvent.SellerId,
                Sku = domainEvent.Sku,
                BasePriceAmount = domainEvent.BasePriceAmount,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}
