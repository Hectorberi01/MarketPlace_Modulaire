namespace Marketplace.Modules.Settlement.Contracts;

/// <summary>Soldes du portefeuille d'un vendeur.</summary>
public sealed record SellerWalletView(
    Guid SellerId,
    decimal PendingBalance,
    decimal AvailableBalance,
    // Somme des retraits EN COURS DE ROUTE : demandés (attente de validation admin) ET
    // en cours de versement chez le PSP. Dans les deux cas les fonds sont déjà retenus,
    // retirés du solde principal. Ne compter que les premiers ferait « disparaître »
    // l'argent de l'écran du vendeur entre la validation et la confirmation du PSP.
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

/// <summary>
/// Retrait dont le versement a été demandé au PSP mais N'EST PAS confirmé (vue admin).
///
/// C'est l'état qui mérite une surveillance humaine : tant qu'il dure, le vendeur est
/// débité sans avoir reçu son argent. <paramref name="Anomaly"/> porte l'incident
/// éventuel (timeout…), et un <paramref name="ProviderRef"/> ABSENT signale le cas le
/// plus grave : versement peut-être parti, mais impossible à réconcilier
/// automatiquement — il faut le retrouver dans le tableau de bord du PSP.
/// </summary>
public sealed record ProcessingWithdrawalView(
    Guid Id,
    Guid SellerId,
    string SellerName,
    decimal Amount,
    string Currency,
    string? ProviderRef,
    string? Anomaly,
    DateTime CreatedAtUtc,
    DateTime? SentToPspAtUtc);

/// <summary>Soldes du portefeuille de la plateforme (admin).</summary>
public sealed record PlatformWalletView(
    decimal CommissionBalance,
    decimal ProviderFeeBalance,
    decimal ShippingBalance,
    decimal RefundsBalance,
    string Currency);

/// <summary>Un remboursement client direct (versement MoMo initié par l'admin).</summary>
public sealed record CustomerRefundView(
    Guid Id,
    Guid OrderId,
    Guid BuyerId,
    decimal Amount,
    string Currency,
    string Reason,
    string Msisdn,
    string Provider,
    string Status,
    string? ProviderRef,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

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
