using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Cart.Domain.Carts.Events;

namespace Marketplace.Modules.Cart.Domain.Carts;

/// <summary>
/// Panier d'un acheteur. Garde un snapshot des lignes (offre + prix de base) ;
/// le prix effectif avec promotions est calculé à la volée par Pricing. Une
/// seule devise par panier. Agrégat racine : possède ses lignes.
/// </summary>
public sealed class Cart : AggregateRoot<CartId>
{
    private readonly List<CartItem> _items = new();

    private Cart()
    {
    }

    private Cart(CartId id, Guid buyerId, string currency)
        : base(id)
    {
        BuyerId = buyerId;
        Currency = currency;
        Status = CartStatus.Active;

        Raise(new CartCreatedDomainEvent(id.Value, buyerId));
    }

    public Guid BuyerId { get; private set; }
    public string Currency { get; private set; } = default!;
    public CartStatus Status { get; private set; }
    public IReadOnlyCollection<CartItem> Items => _items.AsReadOnly();

    public static Result<Cart> Create(Guid buyerId, string currency)
    {
        if (buyerId == Guid.Empty)
        {
            return Error.Validation("cart.buyer_required", "L'acheteur est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            return Error.Validation("cart.currency_invalid", "La devise doit être un code ISO à 3 lettres.");
        }

        return new Cart(CartId.New(), buyerId, currency.Trim().ToUpperInvariant());
    }

    public Result AddItem(
        Guid offerId, Guid productId, Guid categoryId, Guid sellerId, string sku,
        Guid shipFromLocationId, decimal unitBaseAmount, string currency, int quantity)
    {
        if (Status != CartStatus.Active)
        {
            return Result.Failure(Error.Conflict("cart.not_active", "Le panier n'est plus modifiable."));
        }

        if (quantity <= 0)
        {
            return Result.Failure(Error.Validation("cart.quantity_invalid", "La quantité doit être positive."));
        }

        if (!string.Equals(currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.Conflict("cart.currency_mismatch", "L'article n'est pas dans la devise du panier."));
        }

        var existing = _items.FirstOrDefault(i => i.OfferId == offerId);
        if (existing is not null)
        {
            existing.IncreaseQuantity(quantity);
        }
        else
        {
            _items.Add(new CartItem(
                Guid.NewGuid(), offerId, productId, categoryId, sellerId, sku,
                shipFromLocationId, unitBaseAmount, Currency, quantity));
        }

        Raise(new ItemAddedToCartDomainEvent(Id.Value, offerId, quantity));
        return Result.Success();
    }

    public Result UpdateItemQuantity(Guid offerId, int quantity)
    {
        if (Status != CartStatus.Active)
        {
            return Result.Failure(Error.Conflict("cart.not_active", "Le panier n'est plus modifiable."));
        }

        var item = _items.FirstOrDefault(i => i.OfferId == offerId);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("cart.item.not_found", "Article absent du panier."));
        }

        if (quantity <= 0)
        {
            _items.Remove(item);
            return Result.Success();
        }

        item.SetQuantity(quantity);
        return Result.Success();
    }

    public Result RemoveItem(Guid offerId)
    {
        var item = _items.FirstOrDefault(i => i.OfferId == offerId);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("cart.item.not_found", "Article absent du panier."));
        }

        _items.Remove(item);
        return Result.Success();
    }

    public void Clear() => _items.Clear();

    public Result MarkCheckedOut()
    {
        if (Status != CartStatus.Active)
        {
            return Result.Failure(Error.Conflict("cart.not_active", "Le panier n'est plus actif."));
        }

        if (_items.Count == 0)
        {
            return Result.Failure(Error.Conflict("cart.empty", "Impossible de valider un panier vide."));
        }

        Status = CartStatus.CheckedOut;
        Raise(new CartCheckedOutDomainEvent(Id.Value, BuyerId));
        return Result.Success();
    }
}
