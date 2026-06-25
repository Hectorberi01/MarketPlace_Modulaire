using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Ordering.Domain.Orders;

/// <summary>
/// Données d'une ligne à figer au moment de la commande (snapshot prix issu de
/// Pricing). Sert d'entrée à la création de la commande.
/// </summary>
public sealed record OrderLineDraft(
    Guid OfferId,
    Guid ProductId,
    Guid SellerId,
    string Sku,
    Guid ShipFromLocationId,
    int Quantity,
    decimal UnitBasePrice,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalUnitPrice);

/// <summary>
/// Ligne de commande : un snapshot figé du prix au moment de l'achat (prix de
/// base, réductions par financeur, prix final). Les prix ne bougent plus après
/// la commande — base d'un payout vendeur auditable. Entité enfant de Order.
/// </summary>
public sealed class OrderLine : Entity<Guid>
{
    private OrderLine()
    {
    }

    internal OrderLine(Guid id, OrderLineDraft draft)
        : base(id)
    {
        OfferId = draft.OfferId;
        ProductId = draft.ProductId;
        SellerId = draft.SellerId;
        Sku = draft.Sku;
        ShipFromLocationId = draft.ShipFromLocationId;
        Quantity = draft.Quantity;
        UnitBasePrice = draft.UnitBasePrice;
        SellerDiscount = draft.SellerDiscount;
        PlatformDiscount = draft.PlatformDiscount;
        FinalUnitPrice = draft.FinalUnitPrice;
    }

    public Guid OfferId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid SellerId { get; private set; }
    public string Sku { get; private set; } = default!;
    public Guid ShipFromLocationId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitBasePrice { get; private set; }
    public decimal SellerDiscount { get; private set; }
    public decimal PlatformDiscount { get; private set; }
    public decimal FinalUnitPrice { get; private set; }

    /// <summary>Total payé pour la ligne (prix final unitaire × quantité).</summary>
    public decimal LineTotal => FinalUnitPrice * Quantity;
}
