namespace Marketplace.Modules.Sellers.Contracts;

/// <summary>Vue publique d'un vendeur, exposée aux autres modules et au front.</summary>
public sealed record SellerSummary(
    Guid Id,
    Guid UserId,
    string ShopName,
    string? LogoUrl,
    string? Description,
    string Status,
    string KybStatus,
    decimal CommissionRate,
    decimal Rating,
    int SalesCount,
    PayoutAccountSummary? Payout,
    IReadOnlyList<KybDocumentSummary> KybDocuments);

public sealed record KybDocumentSummary(
    Guid Id,
    string Type,
    string FileUrl,
    // Statut par pièce, dérivé de la vérification : Verified si la pièce est
    // vérifiée, Rejected si la boutique est refusée, sinon InReview.
    string Status,
    DateTime UploadedAtUtc,
    DateTime? VerifiedAtUtc);

public sealed record PayoutAccountSummary(
    string Provider,
    string AccountNumber,
    string AccountName);
