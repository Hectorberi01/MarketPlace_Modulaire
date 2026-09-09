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
    private readonly IPromotionRedemptionRepository _redemptionRepository;

    public PricingModuleApi(
        IPromotionRepository promotionRepository,
        IPromotionRedemptionRepository redemptionRepository)
    {
        _promotionRepository = promotionRepository;
        _redemptionRepository = redemptionRepository;
    }

    public async Task<CouponValidation> ValidateCouponAsync(string code, Guid buyerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return CouponValidation.Invalid("pricing.coupon.required", "Le code promo est obligatoire.");
        }

        var promotion = await _promotionRepository.GetByCodeAsync(code, cancellationToken);

        // Message VOLONTAIREMENT identique pour « inexistant » et « inactif/expiré ».
        // Distinguer les deux transformerait cet endpoint en oracle : un attaquant
        // énumérerait les codes valides mais expirés, puis les rejouerait au prochain
        // renouvellement de campagne.
        if (promotion is null || !promotion.IsActiveAt(DateTime.UtcNow))
        {
            return CouponValidation.Invalid("pricing.coupon.invalid", "Ce code promo est invalide ou expiré.");
        }

        if (promotion.PerUserLimit > 0)
        {
            var used = await _redemptionRepository.CountForBuyerAsync(promotion.Id, buyerId, cancellationToken);
            if (used >= promotion.PerUserLimit)
            {
                return CouponValidation.Invalid(
                    "pricing.coupon.per_user_limit_reached",
                    "Vous avez déjà utilisé ce code le nombre de fois autorisé.");
            }
        }

        return CouponValidation.Valid();
    }

    public async Task<PriceBreakdownDto> CalculatePriceAsync(PriceRequest request, CancellationToken cancellationToken = default)
    {
        var basePriceResult = Money.Create(request.LineAmount, request.Currency);
        if (basePriceResult.IsFailure)
        {
            // Devise/montant invalide : on renvoie le prix de base sans réduction.
            return new PriceBreakdownDto(request.LineAmount, 0m, 0m, request.LineAmount, request.Currency);
        }

        var nowUtc = DateTime.UtcNow;
        var candidates = await _promotionRepository.GetActiveCandidatesAsync(
            request.ProductId, request.CategoryId, request.SellerId, nowUtc, cancellationToken);

        // Le contexte porte les valeurs du PANIER (sous-total, quantité totale) : ce
        // sont elles qu'interrogent les conditions d'éligibilité. Les identifiants,
        // eux, restent ceux de la ligne — c'est la portée de la promotion qui les lit.
        var context = new PricingContext(
            request.ProductId, request.CategoryId, request.SellerId, request.TotalQuantity,
            request.CartSubtotal, request.IsFirstOrder, request.Code, nowUtc);

        var breakdownResult = PriceCalculator.Calculate(basePriceResult.Value, candidates, context);
        if (breakdownResult.IsFailure)
        {
            return new PriceBreakdownDto(request.LineAmount, 0m, 0m, request.LineAmount, request.Currency);
        }

        var b = breakdownResult.Value;
        return new PriceBreakdownDto(
            b.BasePrice.Amount, b.SellerDiscount.Amount, b.PlatformDiscount.Amount, b.FinalPrice.Amount, b.FinalPrice.Currency);
    }

    public async Task<CartDiscountDto> CalculateCartDiscountAsync(
        CartDiscountRequest request, CancellationToken cancellationToken = default)
    {
        if (request.CartSubtotal <= 0m)
        {
            return CartDiscountDto.None;
        }

        var nowUtc = DateTime.UtcNow;

        // Aucun produit, aucune catégorie, aucun vendeur : une remise d'ORDRE ne vise
        // pas un article. Les identifiants vides font échouer les portées ciblées, ce
        // qui est exactement le comportement voulu — seules les portées « panier » et
        // « place entière » répondent.
        var candidates = await _promotionRepository.GetActiveCandidatesAsync(
            Guid.Empty, Guid.Empty, Guid.Empty, nowUtc, cancellationToken);

        var context = new PricingContext(
            Guid.Empty, Guid.Empty, Guid.Empty, request.TotalQuantity,
            request.CartSubtotal, request.IsFirstOrder, request.Code, nowUtc);

        // La devise de la demande décide de la précision de la remise : le calcul
        // arrondissait à deux décimales en dur, ce qui produisait en XOF des centimes
        // que la commande transportait jusqu'à une passerelle qui les tronquait.
        var (seller, platform) = PriceCalculator.CalculateCartLevel(
            request.CartSubtotal, candidates, context, request.Currency);
        return new CartDiscountDto(seller, platform);
    }
}
