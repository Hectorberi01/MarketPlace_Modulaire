using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Application.Offers;

internal static class OfferMapper
{
    public static OfferSummary ToSummary(Offer offer) => new(
        offer.Id.Value,
        offer.ProductId,
        offer.SellerId,
        offer.VariantSku.Value,
        offer.BasePrice.Amount,
        offer.BasePrice.Currency,
        offer.Condition.ToString(),
        offer.FulfillmentType.ToString(),
        offer.ShipFromLocationId,
        offer.HandlingTime,
        offer.Status.ToString(),
        offer.SellerPrice,
        offer.CommissionAmount,
        offer.ProviderFeeAmount,
        offer.BasePrice.Amount,
        offer.CompareAtPrice?.Amount,
        offer.DiscountEndsOnUtc);
}
