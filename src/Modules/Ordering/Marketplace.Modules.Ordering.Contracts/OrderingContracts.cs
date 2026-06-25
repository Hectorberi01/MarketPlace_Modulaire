namespace Marketplace.Modules.Ordering.Contracts;

/// <summary>Ligne de commande figée (snapshot prix par financeur).</summary>
public sealed record OrderLineSummary(
    Guid OfferId,
    Guid ProductId,
    Guid SellerId,
    string Sku,
    Guid ShipFromLocationId,
    int Quantity,
    decimal UnitBasePrice,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalUnitPrice,
    decimal LineTotal);

/// <summary>Vue publique d'une commande.</summary>
public sealed record OrderSummary(
    Guid Id,
    Guid BuyerId,
    Guid CartId,
    string Currency,
    string Status,
    DateTime CreatedAtUtc,
    decimal Subtotal,
    decimal TotalSellerDiscount,
    decimal TotalPlatformDiscount,
    decimal GrandTotal,
    IReadOnlyList<OrderLineSummary> Lines);
