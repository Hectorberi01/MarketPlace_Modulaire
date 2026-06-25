using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Catalog.Contracts.IntegrationEvents;
using Marketplace.Modules.Catalog.Domain.Products.Events;

namespace Marketplace.Modules.Catalog.Application.Products.EventHandlers;

/// <summary>
/// Frontière domaine -> intégration : à la création d'un produit, on publie
/// l'IntegrationEvent public sur le bus (via l'outbox). C'est ici que le fait
/// interne devient un contrat consommable par Search, Inventory, etc.
/// </summary>
public sealed class ProductCreatedDomainEventHandler : IDomainEventHandler<ProductCreatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ProductCreatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(ProductCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var integrationEvent = new ProductCreatedIntegrationEvent
        {
            ProductId = domainEvent.ProductId,
            SellerId = domainEvent.SellerId,
            CategoryId = domainEvent.CategoryId,
            Name = domainEvent.Name,
            Slug = domainEvent.Slug
        };

        return _publisher.PublishAsync(integrationEvent, cancellationToken);
    }
}
