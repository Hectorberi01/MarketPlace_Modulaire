namespace Marketplace.Modules.Shipping.Contracts;

/// <summary>Vue publique d'un transporteur du catalogue.</summary>
public sealed record CarrierSummary(
    Guid Id,
    string Name,
    string Code,
    string? TrackingUrlTemplate,
    string? LogoUrl,
    bool IsActive,
    DateTime CreatedAtUtc);

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
    string? TrackingUrl,
    DateTime CreatedAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    IReadOnlyList<ShipmentItemSummary> Items);
