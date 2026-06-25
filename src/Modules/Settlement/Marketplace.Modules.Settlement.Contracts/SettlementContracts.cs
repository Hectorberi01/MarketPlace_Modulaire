namespace Marketplace.Modules.Settlement.Contracts;

/// <summary>Reversement à un vendeur dans un lot.</summary>
public sealed record PayoutSummary(
    Guid Id,
    Guid SellerId,
    decimal GrossAmount,
    decimal CommissionAmount,
    decimal NetAmount,
    string Currency,
    string Status,
    string? ProviderRef,
    DateTime? PaidAtUtc);

/// <summary>Lot de reversements d'une période.</summary>
public sealed record SettlementBatchSummary(
    Guid Id,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    string Currency,
    decimal TotalNet,
    string Status,
    DateTime CreatedAtUtc,
    IReadOnlyList<PayoutSummary> Payouts);

/// <summary>Relevé d'un vendeur sur une période (ventes, commissions, net).</summary>
public sealed record SellerStatementSummary(
    Guid SellerId,
    decimal GrossSales,
    decimal Commissions,
    decimal NetPayout,
    string Currency,
    int LineCount);
