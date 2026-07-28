namespace Marketplace.Modules.Settlement.Domain.Earnings;

/// <summary>Agrégat des gains d'un vendeur sur une période (relevé).</summary>
public sealed record SellerStatement(
    Guid SellerId, decimal GrossSales, decimal Commissions, decimal NetPayout, string Currency, int LineCount);

public interface ISellerEarningRepository
{
    Task AddAsync(SellerEarning earning, CancellationToken cancellationToken = default);

    /// <summary>Vrai si la commande a déjà été comptabilisée (idempotence des events).</summary>
    Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Gains accumulés (non soldés) créés dans la période.</summary>
    Task<IReadOnlyList<SellerEarning>> ListAccruedInPeriodAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    /// <summary>Gains libérés (escrow levé à la livraison) et donc payables, dans la période.</summary>
    Task<IReadOnlyList<SellerEarning>> ListReleasedInPeriodAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    /// <summary>Gains rattachés à une commande (pour libération à la livraison).</summary>
    Task<IReadOnlyList<SellerEarning>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Gains rattachés à un lot de reversement (pour annuler le lot).</summary>
    Task<IReadOnlyList<SellerEarning>> ListByBatchAsync(Guid settlementBatchId, CancellationToken cancellationToken = default);

    Task<SellerStatement> GetSellerStatementAsync(Guid sellerId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lignes détaillées du relevé d'un vendeur : chaque gain créé sur la période,
    /// trié par date de création. Sert à produire les écritures détaillées
    /// (vente + commission) du relevé, sans agrégation.
    /// </summary>
    Task<IReadOnlyList<SellerEarning>> ListSellerEarningsAsync(Guid sellerId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
}
