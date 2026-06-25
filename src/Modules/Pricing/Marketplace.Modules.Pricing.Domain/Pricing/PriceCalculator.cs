using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Pricing.Domain.Promotions;

namespace Marketplace.Modules.Pricing.Domain.Pricing;

/// <summary>Contexte d'achat servant à l'application des promotions.</summary>
public sealed record PricingContext(
    Guid ProductId,
    Guid CategoryId,
    Guid SellerId,
    int Quantity,
    decimal Subtotal,
    bool IsFirstOrder,
    string? Code,
    DateTime NowUtc);

/// <summary>
/// Service de domaine qui calcule le prix effectif : il empile les promotions
/// applicables sur le prix de base (jamais modifié) et trace le financeur de
/// chaque réduction (vendeur vs plateforme) dans un PriceBreakdown auditable.
/// </summary>
public static class PriceCalculator
{
    public static Result<PriceBreakdown> Calculate(
        Money basePrice,
        IReadOnlyList<Promotion> promotions,
        PricingContext context)
    {
        var effective = basePrice.Amount;
        var sellerDiscount = 0m;
        var platformDiscount = 0m;

        foreach (var promotion in promotions)
        {
            if (!promotion.IsActiveAt(context.NowUtc))
            {
                continue;
            }

            if (!promotion.AppliesTo(
                    context.ProductId, context.CategoryId, context.SellerId,
                    context.Subtotal, context.Quantity, context.IsFirstOrder, context.Code))
            {
                continue;
            }

            var discount = Math.Min(promotion.ComputeDiscount(effective), effective);
            if (discount <= 0m)
            {
                continue;
            }

            switch (promotion.FundedBy)
            {
                case FundedBy.Seller:
                    sellerDiscount += discount;
                    break;
                case FundedBy.Platform:
                    platformDiscount += discount;
                    break;
                case FundedBy.Shared:
                    var sellerShare = decimal.Round(discount / 2m, 2);
                    sellerDiscount += sellerShare;
                    platformDiscount += discount - sellerShare;
                    break;
            }

            effective -= discount;
        }

        return PriceBreakdown.Create(basePrice.Amount, sellerDiscount, platformDiscount, basePrice.Currency);
    }
}
