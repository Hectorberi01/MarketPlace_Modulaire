using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Cart.Application.Abstractions;
using Marketplace.Modules.Cart.Application.Carts;
using Marketplace.Modules.Cart.Domain.Carts;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Offers.Contracts;
using CartAggregate = Marketplace.Modules.Cart.Domain.Carts.Cart;

namespace Marketplace.Modules.Cart.Application.Carts.Commands.AddItem;

/// <summary>
/// Résout l'offre (Offers), sa catégorie (Catalog) et vérifie la disponibilité
/// (Inventory) via les seules APIs in-process, puis ajoute la ligne au panier
/// actif — créé à la volée dans la devise de l'offre s'il n'existe pas.
/// </summary>
internal sealed class AddItemToCartCommandHandler : ICommandHandler<AddItemToCartCommand, Guid>
{
    private readonly ICartRepository _cartRepository;
    private readonly IOfferModuleApi _offerModuleApi;
    private readonly ICatalogModuleApi _catalogModuleApi;
    private readonly IInventoryModuleApi _inventoryModuleApi;
    private readonly ICartUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public AddItemToCartCommandHandler(
        ICartRepository cartRepository,
        IOfferModuleApi offerModuleApi,
        ICatalogModuleApi catalogModuleApi,
        IInventoryModuleApi inventoryModuleApi,
        ICartUnitOfWork unitOfWork,
        ICacheService cache)
    {
        _cartRepository = cartRepository;
        _offerModuleApi = offerModuleApi;
        _catalogModuleApi = catalogModuleApi;
        _inventoryModuleApi = inventoryModuleApi;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<Result<Guid>> Handle(AddItemToCartCommand command, CancellationToken cancellationToken)
    {
        var offer = await _offerModuleApi.GetOfferAsync(command.OfferId, cancellationToken);
        if (offer is null)
        {
            return Result.Failure<Guid>(Error.NotFound("cart.offer.not_found", "Offre introuvable."));
        }

        if (!string.Equals(offer.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<Guid>(Error.Conflict("cart.offer.not_active", "L'offre n'est pas disponible à la vente."));
        }

        var product = await _catalogModuleApi.GetProductAsync(offer.ProductId, cancellationToken);
        if (product is null)
        {
            return Result.Failure<Guid>(Error.NotFound("cart.product.not_found", "Produit introuvable."));
        }

        if (!await _inventoryModuleApi.IsInStockAsync(offer.Sku, command.Quantity, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict("cart.out_of_stock", "Stock insuffisant pour cette quantité."));
        }

        var cart = await _cartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            var created = CartAggregate.Create(command.BuyerId, offer.Currency);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error);
            }

            cart = created.Value;
            await _cartRepository.AddAsync(cart, cancellationToken);
        }

        var addResult = cart.AddItem(
            offer.Id, offer.ProductId, product.CategoryId, offer.SellerId, offer.Sku,
            offer.ShipFromLocationId, offer.BasePriceAmount, offer.Currency, command.Quantity);

        if (addResult.IsFailure)
        {
            return Result.Failure<Guid>(addResult.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CartCacheKeys.Active(command.BuyerId), cancellationToken);
        return cart.Id.Value;
    }
}
