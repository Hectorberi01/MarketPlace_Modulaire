namespace Marketplace.Modules.Settlement.Contracts;

/// <summary>Soldes du portefeuille d'un vendeur.</summary>
public sealed record SellerWalletView(
    Guid SellerId,
    decimal PendingBalance,
    decimal AvailableBalance,
    // Somme des retraits demandés en attente de validation admin (fonds déjà
    // retenus, retirés du solde principal).
    decimal PendingWithdrawal,
    string Currency);

/// <summary>Demande de retrait en attente de validation (vue admin, enrichie vendeur).</summary>
public sealed record PendingWithdrawalView(
    Guid Id,
    Guid SellerId,
    string SellerName,
    decimal Amount,
    string Currency,
    string? PayoutProvider,
    string? PayoutAccountNumber,
    DateTime CreatedAtUtc);

/// <summary>Soldes du portefeuille de la plateforme (admin).</summary>
public sealed record PlatformWalletView(
    decimal CommissionBalance,
    decimal ProviderFeeBalance,
    decimal ShippingBalance,
    string Currency);

/// <summary>Une ligne du grand livre wallet (relevé vendeur / plateforme).</summary>
public sealed record WalletTransactionView(
    Guid Id,
    string Account,
    string Direction,
    decimal Amount,
    string Currency,
    string Reason,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTime CreatedAtUtc);

/// <summary>Une demande de retrait vendeur.</summary>
public sealed record WithdrawalView(
    Guid Id,
    Guid SellerId,
    decimal Amount,
    string Currency,
    string Status,
    string? ProviderRef,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);
