namespace Marketplace.Modules.Delivery.Contracts;

/// <summary>Le compte Mobile Money d'un coursier, vu de l'extérieur du module.</summary>
public sealed record CourierPayoutSummary(string Provider, string AccountNumber, string AccountName);

/// <summary>
/// Vue publique d'un coursier. Volontairement pauvre : elle porte de quoi le payer et
/// de quoi décider s'il a le droit de l'être, rien de plus. Sa position, sa zone et sa
/// disponibilité appartiennent au service coursier.
/// </summary>
public sealed record CourierSummary(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string Phone,
    string Status,
    bool CanBePaid,
    string? ExternalRef,
    CourierPayoutSummary? Payout,
    DateTime CreatedAtUtc);
