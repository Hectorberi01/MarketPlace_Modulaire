using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Pricing.Domain.Pricing;

/// <summary>
/// Résultat du calcul de prix : trace chaque réduction et son financeur pour un
/// payout auditable (cf. dossier, PriceBreakdown). On ne modifie jamais le prix
/// de base ; on empile les réductions et on calcule le prix final à la volée.
/// </summary>
public sealed class PriceBreakdown : ValueObject
{
    private PriceBreakdown(Money basePrice, Money sellerDiscount, Money platformDiscount, Money finalPrice)
    {
        BasePrice = basePrice;
        SellerDiscount = sellerDiscount;
        PlatformDiscount = platformDiscount;
        FinalPrice = finalPrice;
    }

    public Money BasePrice { get; }
    public Money SellerDiscount { get; }
    public Money PlatformDiscount { get; }
    public Money FinalPrice { get; }

    public static Result<PriceBreakdown> Create(decimal baseAmount, decimal sellerDiscount, decimal platformDiscount, string currency)
    {
        var totalDiscount = sellerDiscount + platformDiscount;
        var finalAmount = Math.Max(0m, baseAmount - totalDiscount);

        var basePrice = Money.Create(baseAmount, currency);
        var seller = Money.Create(sellerDiscount, currency);
        var platform = Money.Create(platformDiscount, currency);
        var final = Money.Create(finalAmount, currency);

        if (basePrice.IsFailure)
        {
            return Result.Failure<PriceBreakdown>(basePrice.Error);
        }

        if (seller.IsFailure)
        {
            return Result.Failure<PriceBreakdown>(seller.Error);
        }

        if (platform.IsFailure)
        {
            return Result.Failure<PriceBreakdown>(platform.Error);
        }

        return new PriceBreakdown(basePrice.Value, seller.Value, platform.Value, final.Value);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return BasePrice;
        yield return SellerDiscount;
        yield return PlatformDiscount;
        yield return FinalPrice;
    }
}
