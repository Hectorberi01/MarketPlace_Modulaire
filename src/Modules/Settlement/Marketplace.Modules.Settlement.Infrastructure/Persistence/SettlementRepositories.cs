using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Settlement.Domain.Batches;
using Marketplace.Modules.Settlement.Domain.Earnings;

namespace Marketplace.Modules.Settlement.Infrastructure.Persistence;

internal sealed class SellerEarningRepository : ISellerEarningRepository
{
    private readonly SettlementDbContext _dbContext;

    public SellerEarningRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(SellerEarning earning, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings.AddAsync(earning, cancellationToken);

    public async Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings.AnyAsync(e => e.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<SellerEarning>> ListAccruedInPeriodAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.Status == EarningStatus.Accrued && e.CreatedAtUtc >= startUtc && e.CreatedAtUtc <= endUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SellerEarning>> ListReleasedInPeriodAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.Status == EarningStatus.Released && e.CreatedAtUtc >= startUtc && e.CreatedAtUtc <= endUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SellerEarning>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.OrderId == orderId)
            .ToListAsync(cancellationToken);

    public async Task<SellerStatement> GetSellerStatementAsync(Guid sellerId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var lines = await _dbContext.Earnings
            .AsNoTracking()
            .Where(e => e.SellerId == sellerId && e.CreatedAtUtc >= startUtc && e.CreatedAtUtc <= endUtc)
            .ToListAsync(cancellationToken);

        var currency = lines.Count > 0 ? lines[0].Currency : "XOF";
        return new SellerStatement(
            sellerId,
            lines.Sum(e => e.GrossAmount),
            lines.Sum(e => e.CommissionAmount),
            lines.Sum(e => e.NetAmount),
            currency,
            lines.Count);
    }
}

internal sealed class SettlementBatchRepository : ISettlementBatchRepository
{
    private readonly SettlementDbContext _dbContext;

    public SettlementBatchRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(SettlementBatch batch, CancellationToken cancellationToken = default)
        => await _dbContext.Batches.AddAsync(batch, cancellationToken);

    public async Task<SettlementBatch?> GetByIdAsync(SettlementBatchId id, CancellationToken cancellationToken = default)
        => await _dbContext.Batches.Include(b => b.Payouts).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SettlementBatch>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Batches
            .Include(b => b.Payouts)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}
