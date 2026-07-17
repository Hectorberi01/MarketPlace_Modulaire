using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Payments.Domain.Payments;

namespace Marketplace.Modules.Payments.Infrastructure.Persistence;

internal sealed class PaymentRepository : IPaymentRepository
{
    private readonly PaymentsDbContext _dbContext;

    public PaymentRepository(PaymentsDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
        => await _dbContext.Payments.AddAsync(payment, cancellationToken);

    public async Task<Payment?> GetByIdAsync(PaymentId id, CancellationToken cancellationToken = default)
        => await _dbContext.Payments.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Payment?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Payments
            .Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Payment?> GetByProviderReferenceAsync(string providerReference, CancellationToken cancellationToken = default)
        => await _dbContext.Payments
            .FirstOrDefaultAsync(p => p.ProviderReference == providerReference, cancellationToken);

    public async Task<IReadOnlyList<Payment>> ListAllAsync(int take = 200, CancellationToken cancellationToken = default)
        => await _dbContext.Payments
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Payment> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, Guid? id, PaymentStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.Payments.AsNoTracking().AsQueryable();

        if (id is { } g)
        {
            var paymentId = new PaymentId(g);
            baseQuery = baseQuery.Where(p => p.Id == paymentId || p.OrderId == g);
        }

        var statusCounts = await baseQuery
            .GroupBy(p => p.Status)
            .Select(gr => new { Status = gr.Key, Count = gr.Count() })
            .ToListAsync(cancellationToken);

        var filtered = status is { } s ? baseQuery.Where(p => p.Status == s) : baseQuery;

        var total = await filtered.CountAsync(cancellationToken);

        IOrderedQueryable<Payment> ordered = sort switch
        {
            "amount" => desc ? filtered.OrderByDescending(p => p.Amount.Amount) : filtered.OrderBy(p => p.Amount.Amount),
            "status" => desc ? filtered.OrderByDescending(p => p.Status) : filtered.OrderBy(p => p.Status),
            _ => desc ? filtered.OrderByDescending(p => p.CreatedAtUtc) : filtered.OrderBy(p => p.CreatedAtUtc),
        };

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total, statusCounts.ToDictionary(x => x.Status.ToString(), x => x.Count));
    }

    public async Task<(int Total, int CapturedCount, decimal CapturedAmount, int PendingCount, int FailedCount, int RefundedCount, decimal RefundedAmount)> GetStatsAsync(
        Guid? id, CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.Payments.AsNoTracking().AsQueryable();

        if (id is { } g)
        {
            var paymentId = new PaymentId(g);
            baseQuery = baseQuery.Where(p => p.Id == paymentId || p.OrderId == g);
        }

        // Un seul GROUP BY : compteur + somme des montants par statut (Amount est une
        // colonne « amount » via OwnsOne, donc SUM est traduisible en SQL).
        var rows = await baseQuery
            .GroupBy(p => p.Status)
            .Select(gr => new { Status = gr.Key, Count = gr.Count(), Amount = gr.Sum(x => x.Amount.Amount) })
            .ToListAsync(cancellationToken);

        int cnt(PaymentStatus s) => rows.Where(r => r.Status == s).Sum(r => r.Count);
        decimal amt(PaymentStatus s) => rows.Where(r => r.Status == s).Sum(r => r.Amount);

        return (
            rows.Sum(r => r.Count),
            cnt(PaymentStatus.Captured), amt(PaymentStatus.Captured),
            cnt(PaymentStatus.Pending),
            cnt(PaymentStatus.Failed),
            cnt(PaymentStatus.Refunded), amt(PaymentStatus.Refunded));
    }
}
