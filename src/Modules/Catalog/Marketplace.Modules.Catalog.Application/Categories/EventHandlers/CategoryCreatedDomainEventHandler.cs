using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Catalog.Contracts.IntegrationEvents;
using Marketplace.Modules.Catalog.Domain.Categories.Events;

namespace Marketplace.Modules.Catalog.Application.Categories.EventHandlers;

public sealed class CategoryCreatedDomainEventHandler : IDomainEventHandler<CategoryCreatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public CategoryCreatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(CategoryCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new CategoryCreatedIntegrationEvent
            {
                CategoryId = domainEvent.CategoryId,
                ParentId = domainEvent.ParentId,
                Name = domainEvent.Name,
                Slug = domainEvent.Slug,
                Path = domainEvent.Path
            },
            cancellationToken);
}
