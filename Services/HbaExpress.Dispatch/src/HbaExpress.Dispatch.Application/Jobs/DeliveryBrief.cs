namespace HbaExpress.Dispatch.Application.Jobs;

/// <summary>
/// Ce que la marketplace envoie quand une commande est prête à partir.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// COUCHE ANTI-CORRUPTION : ces types sont À NOUS, pas à la marketplace.
///
/// Ils ont la forme de son événement `OrderReadyForDelivery`, et c'est normal — c'est
/// ce qu'elle envoie. Mais ils vivent ici, dans ce service, pour que le jour où elle
/// renomme un champ ou en ajoute un, la correction se fasse en un seul endroit : la
/// traduction vers le domaine. Le CDC le demande explicitement (§6), et c'est le prix
/// minimum de deux systèmes qui évoluent à des rythmes différents.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record DeliveryBrief(
    Guid OrderId,
    decimal ShippingFee,
    string Currency,
    IReadOnlyList<PickupBrief> Pickups,
    DropoffBrief Dropoff);

public sealed record PickupBrief(
    Guid ShipmentId,
    Guid SellerId,
    string SellerName,
    string? SellerPhone,
    string CommuneCode,
    string? Quartier,
    string? Landmark,
    string? Line,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<ParcelBrief> Parcels);

public sealed record ParcelBrief(string Sku, int Quantity);

public sealed record DropoffBrief(
    Guid BuyerId,
    string? Recipient,
    string? Phone,
    string CommuneCode,
    string? Quartier,
    string? Landmark,
    string? Line,
    double? Latitude,
    double? Longitude);
