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

    /// <summary>
    /// Code promo saisi par l'acheteur, ou null.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE CHAMP MANQUAIT, ET C'EST POURQUOI AUCUN COUPON N'A JAMAIS FONCTIONNÉ.
    ///
    /// `CartPricer` passait `Code: null` EN DUR à Pricing. Or `Promotion.AppliesTo()`
    /// rejette toute promotion qui exige un code si le code fourni ne correspond pas —
    /// et `null` ne correspond jamais. Toute promotion codée était donc, littéralement,
    /// inapplicable. Le back-office pouvait en créer, l'admin pouvait les activer, et
    /// aucun acheteur au monde ne pouvait en bénéficier : il n'existait aucun chemin
    /// pour transmettre un code depuis le front.
    ///
    /// Le panier est le bon porteur : le code s'applique au panier entier, survit à la
    /// navigation, et sera figé dans la commande au checkout.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string? PromotionCode { get; private set; }

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
        Guid shipFromLocationId, decimal unitBaseAmount, string currency, int quantity,
        decimal commissionRate, decimal providerFeeRate)
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
                shipFromLocationId, unitBaseAmount, Currency, quantity,
                commissionRate, providerFeeRate));
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

    /// <summary>
    /// Applique un code promo au panier. Le code est normalisé (trim + MAJUSCULES), comme
    /// à la création d'une promotion — sans quoi « bienvenue10 » ne trouverait jamais
    /// « BIENVENUE10 ».
    ///
    /// ⚠️ Cette méthode n'ATTESTE de rien : elle ne dit pas que le code existe, qu'il est
    /// actif, ni qu'il est applicable. La validation est faite par le module Pricing, seul
    /// détenteur des promotions — le panier ne doit pas savoir ce qu'est une promotion.
    /// L'appelant (ApplyCouponCommandHandler) valide AVANT d'appeler.
    /// </summary>
    public Result ApplyPromotionCode(string code)
    {
        if (Status != CartStatus.Active)
        {
            return Result.Failure(Error.Conflict("cart.not_active", "Le panier n'est plus modifiable."));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(Error.Validation("cart.promotion_code_required", "Le code promo est obligatoire."));
        }

        PromotionCode = code.Trim().ToUpperInvariant();
        return Result.Success();
    }

    /// <summary>Retire le code promo du panier.</summary>
    public Result RemovePromotionCode()
    {
        if (Status != CartStatus.Active)
        {
            return Result.Failure(Error.Conflict("cart.not_active", "Le panier n'est plus modifiable."));
        }

        PromotionCode = null;
        return Result.Success();
    }

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
