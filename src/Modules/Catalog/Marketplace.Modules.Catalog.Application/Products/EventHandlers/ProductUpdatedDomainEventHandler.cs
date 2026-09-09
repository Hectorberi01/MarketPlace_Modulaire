using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Catalog.Contracts.IntegrationEvents;
using Marketplace.Modules.Catalog.Domain.Products.Events;

namespace Marketplace.Modules.Catalog.Application.Products.EventHandlers;

/// <summary>
/// Frontière domaine -> intégration pour la MISE À JOUR d'un produit. Pendant du
/// handler de création, qui existait seul : le fait « ce produit a changé de nom »
/// n'atteignait aucun autre module.
/// </summary>
public sealed class ProductUpdatedDomainEventHandler : IDomainEventHandler<ProductUpdatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ProductUpdatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(ProductUpdatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ProductUpdatedIntegrationEvent
            {
                ProductId = domainEvent.ProductId,
                SellerId = domainEvent.SellerId,
                CategoryId = domainEvent.CategoryId,
                Name = domainEvent.Name,
                Slug = domainEvent.Slug
            },
            cancellationToken);
}
