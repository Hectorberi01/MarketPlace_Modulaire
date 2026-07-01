using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Catalog.Contracts.IntegrationEvents;
using Marketplace.Modules.Catalog.Domain.Products.Events;

namespace Marketplace.Modules.Catalog.Application.Products.EventHandlers;

/// <summary>
/// Frontière domaine -> intégration : publie le changement de statut produit sur
/// le bus (via l'outbox). Search s'en sert pour gérer la visibilité publique.
/// </summary>
public sealed class ProductStatusChangedDomainEventHandler : IDomainEventHandler<ProductStatusChangedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ProductStatusChangedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(ProductStatusChangedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var integrationEvent = new ProductStatusChangedIntegrationEvent
        {
            ProductId = domainEvent.ProductId,
            Status = domainEvent.Status
        };

        return _publisher.PublishAsync(integrationEvent, cancellationToken);
    }
}
