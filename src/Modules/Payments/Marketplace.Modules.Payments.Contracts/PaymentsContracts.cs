namespace Marketplace.Modules.Payments.Contracts;

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
