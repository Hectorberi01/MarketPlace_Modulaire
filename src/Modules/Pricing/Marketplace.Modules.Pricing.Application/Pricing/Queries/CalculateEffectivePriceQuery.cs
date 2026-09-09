using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Pricing.Contracts;
using Marketplace.Modules.Pricing.Domain.Pricing;
using Marketplace.Modules.Pricing.Domain.Promotions;

namespace Marketplace.Modules.Pricing.Application.Pricing.Queries;

/// <summary>
/// Calcule le prix effectif d'une ligne : prix de base + promotions applicables,
/// avec la trace des financeurs.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// CE CHEMIN NE VOIT PAS LES REMISES À MONTANT FIXE, ET C'EST VOULU.
///
/// Il sert une route de SIMULATION (`PricingEndpoints`), pas le tunnel de commande —
/// lequel passe par `IPricingModuleApi`, qui appelle en plus `CalculateCartDiscountAsync`
/// une fois pour le panier.
///
/// `PriceCalculator.Calculate` exclut désormais les remises à montant fixe : appelées
/// une fois par ligne, elles étaient retirées autant de fois qu'il y avait de lignes.
/// Une simulation de ligne affichera donc un prix SANS le coupon d'ordre. C'est la
/// réponse honnête : un coupon de panier n'a pas de valeur définie sur une ligne
/// isolée, et en inventer une ferait mentir la simulation dans l'autre sens.
///
/// <paramref name="Subtotal"/> décrit le PANIER, pas la ligne : c'est lui que lisent
/// les conditions d'éligibilité (« à partir de 50 000 d'achat »).
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record CalculateEffectivePriceQuery(
    decimal BaseAmount,
    string Currency,
    Guid ProductId,
    Guid CategoryId,
    Guid SellerId,
    int Quantity,
    decimal Subtotal,
    string? Code,
    bool IsFirstOrder) : IQuery<PriceBreakdownDto>;

internal sealed class CalculateEffectivePriceQueryHandler : IQueryHandler<CalculateEffectivePriceQuery, PriceBreakdownDto>
{
    private readonly IPromotionRepository _promotionRepository;

    public CalculateEffectivePriceQueryHandler(IPromotionRepository promotionRepository)
        => _promotionRepository = promotionRepository;

    public async Task<Result<PriceBreakdownDto>> Handle(CalculateEffectivePriceQuery query, CancellationToken cancellationToken)
    {
        var basePriceResult = Money.Create(query.BaseAmount, query.Currency);
        if (basePriceResult.IsFailure)
        {
            return Result.Failure<PriceBreakdownDto>(basePriceResult.Error);
        }

        var nowUtc = DateTime.UtcNow;
        var candidates = await _promotionRepository.GetActiveCandidatesAsync(
            query.ProductId, query.CategoryId, query.SellerId, nowUtc, cancellationToken);

        var context = new PricingContext(
            query.ProductId, query.CategoryId, query.SellerId, query.Quantity,
            query.Subtotal, query.IsFirstOrder, query.Code, nowUtc);

        var breakdownResult = PriceCalculator.Calculate(basePriceResult.Value, candidates, context);
        if (breakdownResult.IsFailure)
        {
            return Result.Failure<PriceBreakdownDto>(breakdownResult.Error);
        }

        var b = breakdownResult.Value;
        return new PriceBreakdownDto(
            b.BasePrice.Amount,
            b.SellerDiscount.Amount,
            b.PlatformDiscount.Amount,
            b.FinalPrice.Amount,
            b.FinalPrice.Currency);
    }
}
