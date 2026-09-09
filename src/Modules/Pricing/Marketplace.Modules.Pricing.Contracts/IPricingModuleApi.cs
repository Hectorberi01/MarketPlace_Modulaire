namespace Marketplace.Modules.Pricing.Contracts;

/// <summary>
/// API in-process publique du module Pricing. Cart et Ordering l'appellent pour
/// obtenir le prix effectif d'une ligne (avec la trace des financeurs), sans
/// jamais accéder à la base des promotions.
/// </summary>
public interface IPricingModuleApi
{
    Task<PriceBreakdownDto> CalculatePriceAsync(PriceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remise de PANIER — appelée UNE fois, jamais par ligne.
    ///
    /// Complète <see cref="CalculatePriceAsync"/>, qui exclut délibérément les remises
    /// à montant fixe : une remise d'ordre appelée ligne par ligne est retirée autant
    /// de fois qu'il y a de lignes. Les deux appels ensemble couvrent l'ensemble des
    /// promotions ; l'un sans l'autre en oublie une famille entière.
    /// </summary>
    Task<CartDiscountDto> CalculateCartDiscountAsync(CartDiscountRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valide un code promo pour un acheteur, avant de l'attacher au panier.
    /// Verdict indicatif — voir <see cref="CouponValidation"/>.
    /// </summary>
    Task<CouponValidation> ValidateCouponAsync(string code, Guid buyerId, CancellationToken cancellationToken = default);
}
