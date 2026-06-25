namespace Marketplace.Modules.Recommendations.Domain.Recommendations;

/// <summary>Type de recommandation.</summary>
public enum RecommendationType
{
    Similar = 0,
    FrequentlyBoughtTogether = 1,
    Personalized = 2
}

/// <summary>
/// Suggestion personnalisée, calculée par règles ou ML et rafraîchie en
/// arrière-plan. Read model : projeté hors du chemin transactionnel, jamais
/// source de vérité. La clé fonctionnelle est (Type, contexte produit/utilisateur).
/// </summary>
public sealed class Recommendation
{
    private List<Guid> _recommendedProductIds = new();

    private Recommendation()
    {
    }

    private Recommendation(Guid id, RecommendationType type, Guid? contextProductId, Guid? userId, IEnumerable<Guid> recommended, double score)
    {
        Id = id;
        Type = type;
        ContextProductId = contextProductId;
        UserId = userId;
        _recommendedProductIds.AddRange(recommended.Where(p => p != Guid.Empty).Distinct());
        Score = score;
        GeneratedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public RecommendationType Type { get; private set; }
    public Guid? ContextProductId { get; private set; }
    public Guid? UserId { get; private set; }
    public IReadOnlyCollection<Guid> RecommendedProductIds => _recommendedProductIds.AsReadOnly();
    public double Score { get; private set; }
    public DateTime GeneratedAtUtc { get; private set; }

    public static Recommendation Create(RecommendationType type, Guid? contextProductId, Guid? userId, IEnumerable<Guid> recommended, double score)
        => new(Guid.NewGuid(), type, contextProductId, userId, recommended, score);

    /// <summary>Rafraîchit la liste recommandée et le score (recalcul périodique).</summary>
    public void Refresh(IEnumerable<Guid> recommended, double score)
    {
        _recommendedProductIds = recommended.Where(p => p != Guid.Empty).Distinct().ToList();
        Score = score;
        GeneratedAtUtc = DateTime.UtcNow;
    }
}
