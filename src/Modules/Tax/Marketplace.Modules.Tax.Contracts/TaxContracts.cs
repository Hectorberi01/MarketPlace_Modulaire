namespace Marketplace.Modules.Tax.Contracts;

/// <summary>Résultat d'un calcul de taxe.</summary>
public sealed record TaxResult(decimal Amount, decimal TaxAmount, decimal Total, string Type, Guid? AppliedRuleId);

/// <summary>Vue publique d'une règle fiscale.</summary>
public sealed record TaxRuleSummary(
    Guid Id, string Jurisdiction, Guid? CategoryId, decimal Rate, string Type, DateTime EffectiveFromUtc, bool IsActive);
