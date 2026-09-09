namespace Marketplace.Modules.Tax.Contracts;

/// <summary>Résultat d'un calcul de taxe AJOUTÉE à un montant hors taxe.</summary>
public sealed record TaxResult(decimal Amount, decimal TaxAmount, decimal Total, string Type, Guid? AppliedRuleId);

/// <summary>
/// Ventilation d'un montant toutes taxes comprises.
///
/// <paramref name="NetAmount"/> est obtenu par SOUSTRACTION
/// (<paramref name="GrossAmount"/> moins <paramref name="TaxAmount"/>), et non par une
/// seconde division : c'est ce qui garantit que les deux parts redonnent exactement le
/// montant payé, sans centime évaporé à l'arrondi.
///
/// <paramref name="Rate"/> est le taux RETENU, figé avec le résultat. Une commande doit
/// rester lisible des années plus tard, y compris après un changement de barème : sans
/// le taux, une ventilation devient invérifiable.
///
/// Aucune règle applicable donne une taxe nulle, un taux nul et un type « None » —
/// jamais une erreur.
/// </summary>
public sealed record IncludedTaxResult(
    decimal GrossAmount,
    decimal NetAmount,
    decimal TaxAmount,
    decimal Rate,
    string Type,
    Guid? AppliedRuleId);

/// <summary>Vue publique d'une règle fiscale.</summary>
public sealed record TaxRuleSummary(
    Guid Id, string Jurisdiction, Guid? CategoryId, decimal Rate, string Type, DateTime EffectiveFromUtc, bool IsActive);
