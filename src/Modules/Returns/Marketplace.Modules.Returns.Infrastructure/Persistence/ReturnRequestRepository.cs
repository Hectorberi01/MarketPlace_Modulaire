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

    public async Task<bool> ExistsOpenAsync(Guid orderId, Guid offerId, CancellationToken cancellationToken = default)
        => await _dbContext.ReturnRequests.AnyAsync(
            r => r.OrderId == orderId && r.OfferId == offerId
                 && r.Status != ReturnStatus.Rejected && r.Status != ReturnStatus.Refunded, cancellationToken);
}
