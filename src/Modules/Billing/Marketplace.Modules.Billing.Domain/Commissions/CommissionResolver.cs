namespace Marketplace.Modules.Billing.Domain.Commissions;

/// <summary>
/// Service de domaine : choisit la règle de commission active la plus spécifique
/// (Seller &gt; Category &gt; Global) pour un vendeur / une catégorie donnés.
/// </summary>
public static class CommissionResolver
{
    public static CommissionRule? Resolve(
        IEnumerable<CommissionRule> rules, Guid sellerId, Guid categoryId, DateTime nowUtc)
        => rules
            .Where(r => r.IsApplicableAt(nowUtc))
            .Where(r => r.Scope switch
            {
                CommissionScope.Seller => r.TargetId == sellerId,
                CommissionScope.Category => r.TargetId == categoryId,
                CommissionScope.Global => true,
                _ => false
            })
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.EffectiveFromUtc)
            .FirstOrDefault();
}
