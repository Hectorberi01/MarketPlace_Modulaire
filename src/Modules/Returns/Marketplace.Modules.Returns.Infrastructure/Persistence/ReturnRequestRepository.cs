using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Returns.Domain.Returns;

namespace Marketplace.Modules.Returns.Infrastructure.Persistence;

internal sealed class ReturnRequestRepository : IReturnRequestRepository
{
    private readonly ReturnsDbContext _dbContext;

    public ReturnRequestRepository(ReturnsDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(ReturnRequest request, CancellationToken cancellationToken = default)
        => await _dbContext.ReturnRequests.AddAsync(request, cancellationToken);

    public async Task<ReturnRequest?> GetByIdAsync(ReturnRequestId id, CancellationToken cancellationToken = default)
        => await _dbContext.ReturnRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ReturnRequest>> ListByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default)
        => await _dbContext.ReturnRequests
            .Where(r => r.BuyerId == buyerId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReturnRequest>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.ReturnRequests
            .Where(r => r.SellerId == sellerId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReturnRequest>> ListRefundedBySellerAsync(
        Guid sellerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        => await _dbContext.ReturnRequests
            .Where(r => r.SellerId == sellerId
                        && r.Status == ReturnStatus.Refunded
                        && r.ResolvedAtUtc != null
                        && r.ResolvedAtUtc >= fromUtc
                        && r.ResolvedAtUtc < toUtc)
            .OrderBy(r => r.ResolvedAtUtc)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// La file d'attente des versements. Les plus anciens d'abord : ce sont les
    /// clients qui patientent depuis le plus longtemps, et ceux qui ouvriront un
    /// litige en premier.
    /// </summary>
    public async Task<IReadOnlyList<ReturnRequest>> ListPendingRefundsAsync(CancellationToken cancellationToken = default)
        => await _dbContext.ReturnRequests
            .Where(r => r.Status == ReturnStatus.RefundPending)
            .OrderBy(r => r.RefundApprovedAtUtc)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// RefundPending compte comme un retour OUVERT.
    ///
    /// Sans cette précision, un acheteur dont le remboursement est validé mais pas
    /// encore versé pourrait ouvrir un SECOND retour sur la même ligne — et se faire
    /// rembourser deux fois. Le filtre ne rejette explicitement que les états
    /// terminaux : « rejeté » et « versé ».
    /// </summary>
    public async Task<bool> ExistsOpenAsync(Guid orderId, Guid offerId, CancellationToken cancellationToken = default)
        => await _dbContext.ReturnRequests.AnyAsync(
            r => r.OrderId == orderId && r.OfferId == offerId
                 && r.Status != ReturnStatus.Rejected && r.Status != ReturnStatus.Refunded, cancellationToken);

    public async Task<decimal> SumCommittedRefundsForLineAsync(
        Guid orderId, Guid offerId, CancellationToken cancellationToken = default)
        => await SumCommittedAsync(
            r => r.OrderId == orderId && r.OfferId == offerId, cancellationToken);

    public async Task<decimal> SumCommittedRefundsForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
        => await SumCommittedAsync(r => r.OrderId == orderId, cancellationToken);

    /// <summary>
    /// « ENGAGÉ » VEUT DIRE VALIDÉ OU VERSÉ, PAS SEULEMENT VERSÉ.
    ///
    /// `RefundPending` est un remboursement décidé dont l'argent n'est pas encore parti.
    /// L'exclure ferait qu'un second retour, ouvert pendant que l'administrateur exécute
    /// le versement, repartirait sur un plafond encore intact. C'est la même raison qui
    /// fait compter `RefundPending` comme un retour OUVERT dans `ExistsOpenAsync` — un
    /// engagement pris compte à partir de sa décision, pas à partir de son décaissement.
    ///
    /// `Rejected` et les états antérieurs à la décision ne comptent pour rien : aucun
    /// montant n'y a jamais été promis.
    /// </summary>
    private async Task<decimal> SumCommittedAsync(
        System.Linq.Expressions.Expression<Func<ReturnRequest, bool>> scope,
        CancellationToken cancellationToken)
        => await _dbContext.ReturnRequests
            .AsNoTracking()
            .Where(scope)
            .Where(r => r.Status == ReturnStatus.RefundPending || r.Status == ReturnStatus.Refunded)
            // SumAsync sur un decimal? renvoie null si aucune ligne ne correspond ; le
            // `?? 0m` est ce qui distingue « rien à rembourser » d'une exception.
            .SumAsync(r => r.RefundAmount ?? 0m, cancellationToken);
}
