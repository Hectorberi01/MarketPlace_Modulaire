using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Sellers.Contracts.IntegrationEvents;
using Marketplace.Modules.Sellers.Domain.Sellers.Events;

namespace Marketplace.Modules.Sellers.Application.Sellers.EventHandlers;

/// <summary>Publie l'event d'intégration « vendeur onboardé ».</summary>
public sealed class SellerRegisteredDomainEventHandler : IDomainEventHandler<SellerRegisteredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerRegisteredDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerRegisteredDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerRegisteredIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId,
                ShopName = domainEvent.ShopName
            },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « vendeur activé » (Catalog, Notifications…).</summary>
public sealed class SellerActivatedDomainEventHandler : IDomainEventHandler<SellerActivatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerActivatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerActivatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerActivatedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « vendeur fermé » (Catalog retire les produits de la vente).</summary>
public sealed class SellerClosedDomainEventHandler : IDomainEventHandler<SellerClosedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerClosedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerClosedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerClosedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « vendeur réactivé ».</summary>
public sealed class SellerReactivatedDomainEventHandler : IDomainEventHandler<SellerReactivatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerReactivatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerReactivatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerReactivatedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « vendeur supprimé » (Catalog purge les produits).</summary>
public sealed class SellerDeletedDomainEventHandler : IDomainEventHandler<SellerDeletedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerDeletedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerDeletedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerDeletedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}
