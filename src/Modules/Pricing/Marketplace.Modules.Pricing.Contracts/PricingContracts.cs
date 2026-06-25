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
    string Status);
