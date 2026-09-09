namespace Marketplace.Modules.Pricing.Contracts;

/// <summary>
/// Demande de calcul de prix effectif pour UNE LIGNE de panier.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// DEUX MONTANTS, DEUX RÔLES, ET ILS ÉTAIENT CONFONDUS.
///
/// <paramref name="LineAmount"/> est le montant de la LIGNE — prix unitaire multiplié
/// par la quantité. C'était auparavant le prix UNITAIRE, et l'appelant multipliait le
/// résultat par la quantité : une remise à montant fixe de 500 était donc retirée
/// autant de fois qu'il y avait d'articles.
///
/// <paramref name="CartSubtotal"/> est le sous-total du PANIER ENTIER, et il ne sert
/// qu'aux conditions d'éligibilité (« à partir de 50 000 d'achat »). Le champ
/// s'appelait `Subtotal` et recevait le sous-total de la ligne : une promo à seuil
/// était refusée sur un panier de 100 000 fait de dix lignes à 10 000, et accordée sur
/// TOUTES les lignes dès qu'une seule franchissait le seuil.
///
/// <paramref name="TotalQuantity"/> est le nombre d'articles du panier, pour la même
/// raison : une condition « à partir de 3 articles » porte sur le panier.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record PriceRequest(
    decimal LineAmount,
    string Currency,
    Guid ProductId,
    Guid CategoryId,
    Guid SellerId,
    int Quantity,
    decimal CartSubtotal,
    int TotalQuantity,
    string? Code = null,
    bool IsFirstOrder = false);

/// <summary>
/// Demande de calcul de la remise de PANIER — celle qui ne peut pas se calculer ligne
/// par ligne sans être appliquée plusieurs fois.
///
/// Une remise à montant fixe (« −500 sur votre commande ») est par nature une remise
/// d'ordre, pas d'article. Elle est donc calculée UNE fois sur le sous-total, puis
/// répartie sur les lignes par l'appelant — qui seul connaît les lignes et peut faire
/// de la dernière un reste, sans centime perdu à l'arrondi.
/// </summary>
public sealed record CartDiscountRequest(
    decimal CartSubtotal,
    string Currency,
    int TotalQuantity,
    string? Code = null,
    bool IsFirstOrder = false);

/// <summary>
/// Remise de panier, ventilée par financeur. Les deux montants sont à répartir sur les
/// lignes : c'est cette ventilation qui permet au règlement vendeur de rester juste.
/// </summary>
public sealed record CartDiscountDto(decimal SellerDiscount, decimal PlatformDiscount)
{
    public decimal Total => SellerDiscount + PlatformDiscount;

    public static CartDiscountDto None => new(0m, 0m);
}

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
/// ATTENTION : ce verdict est INDICATIF, pas contractuel : entre cette validation et la
/// confirmation de la commande, le dernier exemplaire du coupon peut partir. La
/// consommation réelle — et la seule qui fasse autorité — a lieu dans
/// RedeemPromotionOnOrderConfirmedHandler, sous verrou.
/// </summary>
public sealed record CouponValidation(bool IsValid, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static CouponValidation Valid() => new(true);

    public static CouponValidation Invalid(string code, string message) => new(false, code, message);
}
