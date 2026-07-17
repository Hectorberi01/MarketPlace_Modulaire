namespace Marketplace.Modules.Pricing.Domain.Promotions;

public interface IPromotionRepository
{
    Task AddAsync(Promotion promotion, CancellationToken cancellationToken = default);

    void Remove(Promotion promotion);

    Task<Promotion?> GetByIdAsync(PromotionId id, CancellationToken cancellationToken = default);

    /// <summary>Promotions actives potentiellement applicables à un contexte (filtrage fin fait en mémoire).</summary>
    Task<IReadOnlyList<Promotion>> GetActiveCandidatesAsync(
        Guid productId, Guid categoryId, Guid sellerId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Retrouve une promotion par son code (normalisé en MAJUSCULES).</summary>
    Task<Promotion?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Promotion>> ListAsync(CancellationToken cancellationToken = default);
}
