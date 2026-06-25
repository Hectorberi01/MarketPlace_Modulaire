namespace Marketplace.Modules.Recommendations.Contracts;

/// <summary>Vue publique d'une recommandation.</summary>
public sealed record RecommendationSummary(
    Guid Id, string Type, Guid? ContextProductId, Guid? UserId, IReadOnlyList<Guid> RecommendedProductIds, double Score, DateTime GeneratedAtUtc);
