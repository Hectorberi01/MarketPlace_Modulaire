using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Billing.Domain.Commissions;
using Marketplace.Modules.Billing.Domain.Invoices;

namespace Marketplace.Modules.Billing.Infrastructure.Persistence;

internal sealed class CommissionRuleRepository : ICommissionRuleRepository
{
    private readonly BillingDbContext _dbContext;

    public CommissionRuleRepository(BillingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(CommissionRule rule, CancellationToken cancellationToken = default)
        => await _dbContext.CommissionRules.AddAsync(rule, cancellationToken);

    public async Task<CommissionRule?> GetByIdAsync(CommissionRuleId id, CancellationToken cancellationToken = default)
        => await _dbContext.CommissionRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CommissionRule>> GetCandidatesAsync(Guid sellerId, Guid categoryId, CancellationToken cancellationToken = default)
        => await _dbContext.CommissionRules
            .Where(r => r.IsActive &&
                        (r.Scope == CommissionScope.Global
                         || (r.Scope == CommissionScope.Seller && r.TargetId == sellerId)
                         || (r.Scope == CommissionScope.Category && r.TargetId == categoryId)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CommissionRule>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.CommissionRules.OrderByDescending(r => r.EffectiveFromUtc).ToListAsync(cancellationToken);
}

internal sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly BillingDbContext _dbContext;

    public InvoiceRepository(BillingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default)
        => await _dbContext.Invoices.AddAsync(invoice, cancellationToken);

    public async Task<Invoice?> GetByIdAsync(InvoiceId id, CancellationToken cancellationToken = default)
        => await _dbContext.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Invoice>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Invoices
            .Include(i => i.Lines)
            .Where(i => i.SellerId == sellerId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}
