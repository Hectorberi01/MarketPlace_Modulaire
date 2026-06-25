namespace Marketplace.Modules.Inventory.Contracts;

/// <summary>Vue publique d'un article de stock.</summary>
public sealed record InventoryItemSummary(
    Guid Id,
    string Sku,
    Guid LocationId,
    int OnHand,
    int Reserved,
    int Available,
    int ReorderThreshold,
    bool IsLowStock);

/// <summary>Disponibilité agrégée d'un SKU (toutes localisations).</summary>
public sealed record AvailabilitySummary(string Sku, int TotalAvailable);

/// <summary>Vue publique d'un lieu d'expédition.</summary>
public sealed record FulfillmentLocationSummary(
    Guid Id,
    string Type,
    Guid? OwnerId,
    string Line,
    string City,
    string Country,
    double? Latitude,
    double? Longitude);
