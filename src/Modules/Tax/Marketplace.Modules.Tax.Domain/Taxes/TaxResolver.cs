namespace Marketplace.Modules.Tax.Domain.Taxes;

/// <summary>
/// Service de domaine : choisit la règle fiscale active la plus spécifique pour
/// une juridiction et une catégorie (catégorie ciblée &gt; juridiction entière).
/// </summary>
public static class TaxResolver
{
    public static TaxRule? Resolve(IEnumerable<TaxRule> rules, string jurisdiction, Guid categoryId, DateTime nowUtc)
    {
        var normalized = jurisdiction.Trim().ToUpperInvariant();
        return rules
            .Where(r => r.IsApplicableAt(nowUtc))
            .Where(r => r.Jurisdiction == normalized && (r.CategoryId == categoryId || r.CategoryId is null))
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.EffectiveFromUtc)
            .FirstOrDefault();
    }
}
