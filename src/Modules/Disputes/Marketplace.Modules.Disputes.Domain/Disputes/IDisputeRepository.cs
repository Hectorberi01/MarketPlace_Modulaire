namespace Marketplace.Modules.Disputes.Domain.Disputes;

public interface IDisputeRepository
{
    Task AddAsync(Dispute dispute, CancellationToken cancellationToken = default);

    Task<Dispute?> GetByIdAsync(DisputeId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Dispute>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Dispute>> ListByParticipantAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Litiges portant sur un ensemble de commandes.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Le module ne connaît ni vendeur ni boutique : un litige est rattaché à une
    /// COMMANDE. Pour lister les litiges d'un vendeur, l'appelant résout d'abord ses
    /// commandes, puis interroge cette méthode.
    ///
    /// Sans elle, la seule voie était <see cref="ListAllAsync"/> suivi d'un filtrage
    /// en mémoire — et le plafond de 500 s'appliquait alors AVANT le filtrage. Ce
    /// n'était donc pas « les 500 litiges du vendeur » mais « les 500 derniers de
    /// toute la plateforme », dans lesquels il fallait espérer trouver les siens. Un
    /// litige ouvert et non traité pouvait sortir de la fenêtre et disparaître de
    /// l'écran du vendeur sans que rien ne le signale.
    ///
    /// Le filtre est ici poussé en base : le plafond porte sur le résultat.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    Task<IReadOnlyList<Dispute>> ListByOrdersAsync(
        IReadOnlyCollection<Guid> orderIds, int take = 500, CancellationToken cancellationToken = default);

    /// <summary>Tous les litiges de la plateforme (back-office admin / modération).</summary>
    Task<IReadOnlyList<Dispute>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default);
}
