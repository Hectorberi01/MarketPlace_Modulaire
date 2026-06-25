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
