namespace Marketplace.Modules.Offers.Contracts;

/// <summary>Vue publique d'une offre, exposée aux autres modules (Cart, Pricing…).</summary>
public sealed record OfferSummary(
    Guid Id,
    Guid ProductId,
    Guid SellerId,
    string Sku,
    decimal BasePriceAmount,
    string Currency,
    string Condition,
    string FulfillmentType,
    Guid ShipFromLocationId,
    int HandlingTime,
    string Status,
    decimal SellerPrice,
    decimal CommissionAmount,
    decimal ProviderFeeAmount,
    decimal ProductPrice);
