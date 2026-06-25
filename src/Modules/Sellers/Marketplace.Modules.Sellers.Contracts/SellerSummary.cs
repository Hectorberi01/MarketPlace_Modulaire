namespace Marketplace.Modules.Sellers.Contracts;

/// <summary>Vue publique d'un vendeur, exposée aux autres modules et au front.</summary>
public sealed record SellerSummary(
    Guid Id,
    Guid UserId,
    string ShopName,
    string? LogoUrl,
    string Status,
    string KybStatus,
    decimal CommissionRate,
    decimal Rating,
    int SalesCount,
    IReadOnlyList<KybDocumentSummary> KybDocuments);

public sealed record KybDocumentSummary(
    Guid Id,
    string Type,
    string FileUrl,
    DateTime? VerifiedAtUtc);
