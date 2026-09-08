namespace Marketplace.Modules.Cart.Contracts;

/// <summary>Ligne de panier valorisée : prix de base + réductions tracées par financeur.</summary>
public sealed record CartLineSummary(
    Guid OfferId,
    Guid ProductId,
    Guid CategoryId,
    Guid SellerId,
    string Sku,
    Guid ShipFromLocationId,
    int Quantity,
    decimal UnitBaseAmount,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalUnitPrice,
    decimal LineTotal,
    string Currency,
    // Barème figé à l'ajout au panier, transmis tel quel à la commande.
    decimal CommissionRate = 0m,
    decimal ProviderFeeRate = 0m);

/// <summary>Panier valorisé (snapshot lignes + totaux calculés via Pricing).</summary>
public sealed record CartSummary(
    Guid CartId,
    Guid BuyerId,
    string Currency,
    string Status,
    IReadOnlyList<CartLineSummary> Lines,
    decimal Subtotal,
    decimal TotalSellerDiscount,
    decimal TotalPlatformDiscount,
    decimal GrandTotal,
    /// <summary>Code promo appliqué au panier, ou null. Ordering le fige dans la commande au checkout.</summary>
    string? PromotionCode = null);
