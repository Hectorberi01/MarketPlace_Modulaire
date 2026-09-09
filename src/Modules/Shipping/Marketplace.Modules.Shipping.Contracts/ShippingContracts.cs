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
    /// <summary>
    /// « Courier », « Carrier », ou nul tant que personne n'a pris le colis.
    ///
    /// SANS CE CHAMP, L'ÉCRAN DE SUIVI NE PEUT RIEN DIRE. Une livraison par coursier de
    /// la plateforme n'a ni transporteur ni numéro de suivi — c'est normal, une moto n'en
    /// a pas — et l'application ne pouvait donc pas distinguer « porté par notre
    /// coursier » de « pas encore expédié ». Les deux se lisaient comme un écran vide.
    /// </summary>
    string? DeliveryMode,
    IReadOnlyList<ShipmentItemSummary> Items);
