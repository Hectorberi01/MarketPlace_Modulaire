using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.Modules.Pricing.Contracts;
using Marketplace.Modules.Pricing.Domain.Pricing;
using Marketplace.Modules.Pricing.Domain.Promotions;

namespace Marketplace.Modules.Pricing.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Pricing : calcule le
/// prix effectif d'une ligne pour Cart/Ordering, avec la trace des financeurs.
/// </summary>
internal sealed class PricingModuleApi : IPricingModuleApi
{
    private readonly IPromotionRepository _promotionRepository;

    public PricingModuleApi(IPromotionRepository promotionRepository)
        => _promotionRepository = promotionRepository;

    public async Task<PriceBreakdownDto> CalculatePriceAsync(PriceRequest request, CancellationToken cancellationToken = default)
    {
        var basePriceResult = Money.Create(request.BaseAmount, request.Currency);
        if (basePriceResult.IsFailure)
        {
            // Devise/montant invalide : on renvoie le prix de base sans réduction.
            return new PriceBreakdownDto(request.BaseAmount, 0m, 0m, request.BaseAmount, request.Currency);
        }

        var nowUtc = DateTime.UtcNow;
        var candidates = await _promotionRepository.GetActiveCandidatesAsync(
            request.ProductId, request.CategoryId, request.SellerId, nowUtc, cancellationToken);

        var context = new PricingContext(
            request.ProductId, request.CategoryId, request.SellerId, request.Quantity,
            request.Subtotal, request.IsFirstOrder, request.Code, nowUtc);

        var breakdownResult = PriceCalculator.Calculate(basePriceResult.Value, candidates, context);
        if (breakdownResult.IsFailure)
        {
            return new PriceBreakdownDto(request.BaseAmount, 0m, 0m, request.BaseAmount, request.Currency);
        }

        var b = breakdownResult.Value;
        return new PriceBreakdownDto(
            b.BasePrice.Amount, b.SellerDiscount.Amount, b.PlatformDiscount.Amount, b.FinalPrice.Amount, b.FinalPrice.Currency);
    }
}
