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
    decimal ProductPrice,
    // Barème appliqué à ce prix. Transporté jusqu'à la ligne de commande, où il
    // sert à répartir le brut. Le figer ici évite qu'un changement de barème
    // modifie rétroactivement le net des vendeurs.
    decimal CommissionRate,
    decimal ProviderFeeRate,
    // Remise : prix acheteur d'origine (barré) et fin de promo. Null si pas de remise.
    decimal? CompareAtAmount = null,
    DateTime? DiscountEndsOnUtc = null);
