namespace Marketplace.Modules.Billing.Contracts;

/// <summary>Résultat d'un calcul de commission pour un montant brut.</summary>
public sealed record CommissionResult(
    decimal GrossAmount,
    decimal CommissionAmount,
    decimal NetAmount,
    string Currency,
    Guid? AppliedRuleId);

/// <summary>Vue publique d'une règle de commission.</summary>
public sealed record CommissionRuleSummary(
    Guid Id,
    string Scope,
    Guid? TargetId,
    decimal Rate,
    decimal FixedFee,
    string Currency,
    decimal? MinFee,
    decimal? MaxFee,
    DateTime EffectiveFromUtc,
    bool IsActive);

/// <summary>Vue publique d'une facture vendeur.</summary>
public sealed record InvoiceSummary(
    Guid Id,
    Guid SellerId,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    string Currency,
    decimal TotalAmount,
    string Status);
