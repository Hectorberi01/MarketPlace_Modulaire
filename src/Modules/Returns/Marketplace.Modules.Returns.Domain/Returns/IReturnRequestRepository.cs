namespace Marketplace.Modules.Returns.Domain.Returns;

public interface IReturnRequestRepository
{
    Task AddAsync(ReturnRequest request, CancellationToken cancellationToken = default);

    Task<ReturnRequest?> GetByIdAsync(ReturnRequestId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReturnRequest>> ListByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReturnRequest>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retours REMBOURSÉS d'un vendeur dont la date de résolution (remboursement)
    /// tombe dans [fromUtc, toUtc). Sert au relevé finances (lignes « refund »).
    /// </summary>
    Task<IReadOnlyList<ReturnRequest>> ListRefundedBySellerAsync(
        Guid sellerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remboursements VALIDÉS mais PAS ENCORE VERSÉS — la file de travail de l'admin.
    ///
    /// C'est la liste des clients qui attendent leur argent. Elle doit être vide en
    /// fin de journée : chaque ligne qui y traîne est un litige en préparation.
    /// </summary>
    Task<IReadOnlyList<ReturnRequest>> ListPendingRefundsAsync(CancellationToken cancellationToken = default);

    /// <summary>Un seul retour ouvert par ligne de commande.</summary>
    Task<bool> ExistsOpenAsync(Guid orderId, Guid offerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Somme des remboursements ENGAGÉS sur une ligne : validés (versement en attente)
    /// et versés.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// `ExistsOpenAsync` NE SUFFISAIT PAS, ET LA DIFFÉRENCE COÛTE DE L'ARGENT.
    ///
    /// Elle interdit un SECOND retour OUVERT sur une ligne — ce qui est juste, et bien
    /// raisonné. Mais elle exclut délibérément l'état « remboursé », si bien qu'une
    /// fois le versement effectué, un nouveau retour repartait sur la même ligne avec
    /// le MÊME plafond. Rien ne bornait le cumul : la ligne était remboursable autant
    /// de fois qu'on ouvrait de retours, et l'index `(OrderId, OfferId)` n'est pas
    /// unique.
    ///
    /// Le cas EN COURS avait été fermé ; c'est le cas CUMULÉ qui restait ouvert.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<decimal> SumCommittedRefundsForLineAsync(
        Guid orderId, Guid offerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Somme des remboursements engagés sur TOUTE la commande, toutes lignes confondues.
    ///
    /// Sert au plafond commun avec le remboursement direct de l'administrateur : les
    /// deux circuits doivent se voir, sinon chacun autorise jusqu'au total de la
    /// commande et l'addition en verse le double.
    /// </summary>
    Task<decimal> SumCommittedRefundsForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default);
}
