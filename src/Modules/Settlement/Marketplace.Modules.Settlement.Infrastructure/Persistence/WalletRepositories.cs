using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Infrastructure.Persistence;

internal sealed class SellerWalletRepository : ISellerWalletRepository
{
    private readonly SettlementDbContext _dbContext;

    public SellerWalletRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    public async Task<SellerWallet?> GetBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.SellerWallets.FirstOrDefaultAsync(w => w.SellerId == sellerId, cancellationToken);

    public async Task AddAsync(SellerWallet wallet, CancellationToken cancellationToken = default)
        => await _dbContext.SellerWallets.AddAsync(wallet, cancellationToken);
}

internal sealed class PlatformWalletRepository : IPlatformWalletRepository
{
    private readonly SettlementDbContext _dbContext;

    public PlatformWalletRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    public async Task<PlatformWallet?> GetAsync(CancellationToken cancellationToken = default)
        => await _dbContext.PlatformWallets.FirstOrDefaultAsync(w => w.Id == PlatformWallet.SingletonId, cancellationToken);

    public async Task AddAsync(PlatformWallet wallet, CancellationToken cancellationToken = default)
        => await _dbContext.PlatformWallets.AddAsync(wallet, cancellationToken);
}

internal sealed class WithdrawalRepository : IWithdrawalRepository
{
    private readonly SettlementDbContext _dbContext;

    public WithdrawalRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Withdrawal withdrawal, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals.AddAsync(withdrawal, cancellationToken);

    public async Task<IReadOnlyList<Withdrawal>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .AsNoTracking()
            .Where(w => w.SellerId == sellerId)
            .OrderByDescending(w => w.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}

internal sealed class WalletTransactionRepository : IWalletTransactionRepository
{
    private readonly SettlementDbContext _dbContext;

    public WalletTransactionRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(WalletTransaction transaction, CancellationToken cancellationToken = default)
        => await _dbContext.WalletTransactions.AddAsync(transaction, cancellationToken);

    public async Task<IReadOnlyList<WalletTransaction>> ListByOwnerAsync(Guid ownerId, int take, CancellationToken cancellationToken = default)
        => await _dbContext.WalletTransactions
            .AsNoTracking()
            .Where(t => t.OwnerId == ownerId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(take <= 0 ? 50 : take)
            .ToListAsync(cancellationToken);
}
