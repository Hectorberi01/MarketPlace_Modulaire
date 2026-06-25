using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Pricing.Contracts.IntegrationEvents;
using Marketplace.Modules.Pricing.Domain.Promotions.Events;

namespace Marketplace.Modules.Pricing.Application.Promotions.EventHandlers;

/// <summary>Publie l'IntegrationEvent « promotion créée » (consommé par Marketing/CMS, Search).</summary>
public sealed class PromotionCreatedDomainEventHandler : IDomainEventHandler<PromotionCreatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public PromotionCreatedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(PromotionCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new PromotionCreatedIntegrationEvent
            {
                PromotionId = domainEvent.PromotionId,
                Type = domainEvent.Type,
                ScopeType = domainEvent.ScopeType
            },
            cancellationToken);
}
