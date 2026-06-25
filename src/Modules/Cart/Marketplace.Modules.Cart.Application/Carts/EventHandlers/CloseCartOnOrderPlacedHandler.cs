using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Cart.Application.Abstractions;
using Marketplace.Modules.Cart.Domain.Carts;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Cart.Application.Carts.EventHandlers;

/// <summary>
/// Réaction (chorégraphie) à « commande placée » : clôt le panier source pour
/// éviter une double commande. Le module Cart ne dépend que des Contracts
/// d'Ordering — jamais de son interne.
/// </summary>
public sealed class CloseCartOnOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    private readonly ICartRepository _cartRepository;
    private readonly ICartUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public CloseCartOnOrderPlacedHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
    {
        _cartRepository = cartRepository;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var cart = await _cartRepository.GetByIdAsync(new CartId(integrationEvent.CartId), cancellationToken);
        if (cart is null || cart.Status != CartStatus.Active)
        {
            return;
        }

        cart.MarkCheckedOut();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CartCacheKeys.Active(integrationEvent.BuyerId), cancellationToken);
    }
}
