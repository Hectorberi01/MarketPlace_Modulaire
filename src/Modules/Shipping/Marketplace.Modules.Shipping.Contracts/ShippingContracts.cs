namespace Marketplace.Modules.Shipping.Contracts;

/// <summary>Ligne d'une expédition.</summary>
public sealed record ShipmentItemSummary(string Sku, int Quantity);

/// <summary>Vue publique d'une expédition.</summary>
public sealed record ShipmentSummary(
    Guid Id,
    Guid OrderId,
    Guid SellerId,
    Guid BuyerId,
    Guid ShipFromLocationId,
    string Status,
    string? Carrier,
    string? TrackingNumber,
    DateTime CreatedAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    IReadOnlyList<ShipmentItemSummary> Items);
