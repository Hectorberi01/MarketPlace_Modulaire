using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Cart.Domain.Carts;

/// <summary>
/// Ligne de panier : un snapshot des identifiants et du prix de base de l'offre
/// au moment de l'ajout. Le prix effectif (promotions) est calculé à la volée
/// par le module Pricing, jamais stocké ici. Entité enfant de l'agrégat Cart.
/// </summary>
public sealed class CartItem : Entity<Guid>
{
    private CartItem()
    {
    }

    internal CartItem(
        Guid id,
        Guid offerId,
        Guid productId,
        Guid categoryId,
        Guid sellerId,
        string sku,
        Guid shipFromLocationId,
        decimal unitBaseAmount,
        string currency,
        int quantity)
        : base(id)
    {
        OfferId = offerId;
        ProductId = productId;
        CategoryId = categoryId;
        SellerId = sellerId;
        Sku = sku;
        ShipFromLocationId = shipFromLocationId;
        UnitBaseAmount = unitBaseAmount;
        Currency = currency;
        Quantity = quantity;
    }

    public Guid OfferId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid SellerId { get; private set; }
    public string Sku { get; private set; } = default!;
    public Guid ShipFromLocationId { get; private set; }
    public decimal UnitBaseAmount { get; private set; }
    public string Currency { get; private set; } = default!;
    public int Quantity { get; private set; }

    internal void IncreaseQuantity(int by) => Quantity += by;

    internal void SetQuantity(int quantity) => Quantity = quantity;
}
