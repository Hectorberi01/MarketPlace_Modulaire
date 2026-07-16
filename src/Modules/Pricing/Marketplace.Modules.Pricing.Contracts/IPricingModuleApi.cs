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
    /// Valide un code promo pour un acheteur, avant de l'attacher au panier.
    /// Verdict indicatif — voir <see cref="CouponValidation"/>.
    /// </summary>
    Task<CouponValidation> ValidateCouponAsync(string code, Guid buyerId, CancellationToken cancellationToken = default);
}
