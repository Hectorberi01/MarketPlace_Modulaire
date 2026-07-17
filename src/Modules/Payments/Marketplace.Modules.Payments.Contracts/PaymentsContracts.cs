namespace Marketplace.Modules.Payments.Contracts;

/// <summary>
/// Agrégats de paiements pour la console admin, calculés côté SQL sur l'ensemble
/// filtré (montants encaissés / remboursés + compteurs), indépendamment de la page.
/// </summary>
public sealed record PaymentStatsSummary(
    int Total,
    int CapturedCount,
    decimal CapturedAmount,
    int PendingCount,
    int FailedCount,
    int RefundedCount,
    decimal RefundedAmount);

/// <summary>Vue publique d'un paiement.</summary>
public sealed record PaymentSummary(
    Guid Id,
    Guid OrderId,
    Guid BuyerId,
    decimal Amount,
    string Currency,
    string Method,
    string Provider,
    string? ProviderReference,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? CapturedAtUtc);
