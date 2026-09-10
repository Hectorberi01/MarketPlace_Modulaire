namespace Marketplace.Modules.Payments.Domain.Payments;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);

    Task<Payment?> GetByIdAsync(PaymentId id, CancellationToken cancellationToken = default);

    Task<Payment?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Retrouve un paiement par sa référence PSP (corrélation webhook / retour).</summary>
    Task<Payment?> GetByProviderReferenceAsync(string providerReference, CancellationToken cancellationToken = default);

    /// <summary>Liste tous les paiements de la plateforme (back-office admin).</summary>
    Task<IReadOnlyList<Payment>> ListAllAsync(int take = 200, CancellationToken cancellationToken = default);

    /// <summary>
    /// Page de paiements pour la console admin : filtre par statut, recherche par
    /// identifiant (paiement ou commande, GUID exact), tri par date décroissante.
    /// Renvoie le total filtré + la répartition par statut (avant filtre statut).
    /// </summary>
    Task<(IReadOnlyList<Payment> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, Guid? id, PaymentStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Agrégats (compteurs + montants encaissés/remboursés) filtrés par recherche
    /// (identifiant paiement ou commande) et éventuellement bornés dans le temps
    /// (borne haute EXCLUSIVE).
    ///
    /// La fenêtre est arrivée tard : ces compteurs portaient sur TOUT l'historique et
    /// s'affichaient à côté de cartes « 30 jours », sans que rien ne le dise. Un
    /// camembert cumulatif depuis le premier jour ne bouge plus visiblement au bout de
    /// quelques mois — il décore, il n'informe plus.
    /// </summary>
    Task<(int Total, int CapturedCount, decimal CapturedAmount, int PendingCount, int FailedCount, int RefundedCount, decimal RefundedAmount)> GetStatsAsync(
        Guid? id, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);
}
