using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Catalog.Contracts.IntegrationEvents;
using Marketplace.Modules.Catalog.Domain.Brands.Events;

namespace Marketplace.Modules.Catalog.Application.Brands.EventHandlers;

public sealed class BrandCreatedDomainEventHandler : IDomainEventHandler<BrandCreatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public BrandCreatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(BrandCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new BrandCreatedIntegrationEvent
            {
                BrandId = domainEvent.BrandId,
                Name = domainEvent.Name,
                Slug = domainEvent.Slug
            },
            cancellationToken);
}
