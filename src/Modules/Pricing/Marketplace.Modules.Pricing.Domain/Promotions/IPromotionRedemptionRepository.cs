namespace Marketplace.Modules.Pricing.Domain.Promotions;

public interface IPromotionRedemptionRepository
{
    Task AddAsync(PromotionRedemption redemption, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cette commande a-t-elle DÉJÀ consommé cette promotion ?
    ///
    /// Garde de confort, pas de sécurité. Elle évite le coût d'une violation de
    /// contrainte sur le chemin normal (rejeu d'outbox), mais elle ne ferme PAS la
    /// fenêtre entre le SELECT et l'INSERT. C'est l'index unique
    /// (PromotionId, OrderId) qui garantit réellement l'unicité — voir
    /// PromotionRedemptionConfiguration.
    /// </summary>
    Task<bool> ExistsAsync(PromotionId promotionId, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Combien de fois CET acheteur a déjà consommé CETTE promotion.</summary>
    Task<int> CountForBuyerAsync(PromotionId promotionId, Guid buyerId, CancellationToken cancellationToken = default);
}
