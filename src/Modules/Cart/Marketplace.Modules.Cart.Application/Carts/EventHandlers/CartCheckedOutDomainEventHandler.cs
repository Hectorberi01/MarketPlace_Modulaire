using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Cart.Contracts.IntegrationEvents;
using Marketplace.Modules.Cart.Domain.Carts.Events;

namespace Marketplace.Modules.Cart.Application.Carts.EventHandlers;

/// <summary>Publie l'IntegrationEvent « panier validé » (consommé par Ordering / analytics).</summary>
public sealed class CartCheckedOutDomainEventHandler : IDomainEventHandler<CartCheckedOutDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public CartCheckedOutDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(CartCheckedOutDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new CartCheckedOutIntegrationEvent { CartId = domainEvent.CartId, BuyerId = domainEvent.BuyerId },
            cancellationToken);
}
