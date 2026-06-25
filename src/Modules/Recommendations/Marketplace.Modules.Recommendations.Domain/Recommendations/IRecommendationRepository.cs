namespace Marketplace.Modules.Recommendations.Domain.Recommendations;

public interface IRecommendationRepository
{
    Task AddAsync(Recommendation recommendation, CancellationToken cancellationToken = default);

    /// <summary>Recommandation existante pour une clé (contexte produit) — pour upsert.</summary>
    Task<Recommendation?> GetByProductAsync(RecommendationType type, Guid contextProductId, CancellationToken cancellationToken = default);

    /// <summary>Recommandation personnalisée existante d'un utilisateur — pour upsert.</summary>
    Task<Recommendation?> GetByUserAsync(RecommendationType type, Guid userId, CancellationToken cancellationToken = default);
}
