namespace Marketplace.Modules.Pricing.Contracts;

/// <summary>Demande de calcul de prix effectif pour une ligne (offre/variante).</summary>
public sealed record PriceRequest(
    decimal BaseAmount,
    string Currency,
    Guid ProductId,
    Guid CategoryId,
    Guid SellerId,
    int Quantity,
    decimal Subtotal,
    string? Code = null,
    bool IsFirstOrder = false);

/// <summary>
/// Résultat du calcul : prix de base, réductions tracées par financeur, prix
/// final. Indispensable pour un payout vendeur correct et auditable.
/// </summary>
public sealed record PriceBreakdownDto(
    decimal BaseAmount,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalAmount,
    string Currency);

/// <summary>Vue publique d'une promotion.</summary>
public sealed record PromotionSummary(
    Guid Id,
    string OwnerType,
    Guid OwnerId,
    string FundedBy,
    string Type,
    decimal Value,
    string ScopeType,
    IReadOnlyList<Guid> Targets,
    string? Code,
    DateTime StartUtc,
    DateTime EndUtc,
    int UsageLimit,
    int UsedCount,
    string Status,
    /// <summary>Plafond par acheteur. 0 = illimité.</summary>
    int PerUserLimit = 0);

/// <summary>
/// Verdict de validation d'un code promo, AVANT de l'attacher au panier.
///
/// Sert à donner une réponse immédiate et lisible à l'acheteur (« code expiré »,
/// « déjà utilisé »), plutôt que de le laisser attacher un code mort au panier et
/// découvrir au checkout que la remise ne s'est jamais appliquée.
///
/// ⚠️ Ce verdict est INDICATIF, pas contractuel : entre cette validation et la
/// confirmation de la commande, le dernier exemplaire du coupon peut partir. La
/// consommation réelle — et la seule qui fasse autorité — a lieu dans
/// RedeemPromotionOnOrderConfirmedHandler, sous verrou.
/// </summary>
public sealed record CouponValidation(bool IsValid, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static CouponValidation Valid() => new(true);

    public static CouponValidation Invalid(string code, string message) => new(false, code, message);
}
