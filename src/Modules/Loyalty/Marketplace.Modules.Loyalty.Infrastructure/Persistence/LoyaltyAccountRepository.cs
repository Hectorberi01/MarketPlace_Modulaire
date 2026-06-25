using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Loyalty.Domain.Accounts;

namespace Marketplace.Modules.Loyalty.Infrastructure.Persistence;

internal sealed class LoyaltyAccountRepository : ILoyaltyAccountRepository
{
    private readonly LoyaltyDbContext _dbContext;

    public LoyaltyAccountRepository(LoyaltyDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(LoyaltyAccount account, CancellationToken cancellationToken = default)
        => await _dbContext.Accounts.AddAsync(account, cancellationToken);

    public async Task<LoyaltyAccount?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Accounts
            .Include(a => a.Transactions)
            .FirstOrDefaultAsync(a => a.UserId == userId, cancellationToken);
}
