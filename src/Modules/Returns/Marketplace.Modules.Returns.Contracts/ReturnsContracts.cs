namespace Marketplace.Modules.Returns.Contracts;

/// <summary>Vue publique d'une demande de retour.</summary>
public sealed record ReturnRequestSummary(
    Guid Id,
    Guid OrderId,
    Guid OfferId,
    Guid BuyerId,
    Guid SellerId,
    string Reason,
    string Status,
    string Currency,
    decimal? RefundAmount,
    string? Carrier,
    string? TrackingNumber,
    DateTime CreatedAtUtc,
    DateTime? ResolvedAtUtc);

/// <summary>
/// Un remboursement effectif (retour au statut Refunded) : commande concernée,
/// montant remboursé et date du remboursement. Utilisé par le relevé finances.
/// </summary>
public sealed record SellerRefundLine(
    Guid OrderId,
    decimal RefundAmount,
    string Currency,
    DateTime RefundedAtUtc);
