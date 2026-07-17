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

    // Suivi activé : l'entité est mutée puis persistée (validation/refus admin).
    public async Task<Withdrawal?> GetByIdAsync(WithdrawalId id, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Withdrawal>> ListByStatusAsync(WithdrawalStatus status, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .AsNoTracking()
            .Where(w => w.Status == status)
            .OrderBy(w => w.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    // SUIVI activé (pas de AsNoTracking) : la réconciliation mute ces entités.
    public async Task<IReadOnlyList<Withdrawal>> ListProcessingForReconciliationAsync(int take, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .Where(w => w.Status == WithdrawalStatus.Processing)
            .OrderBy(w => w.CreatedAtUtc)
            .Take(take <= 0 ? 50 : take)
            .ToListAsync(cancellationToken);

    // SUIVI activé : le webhook mute l'entité juste après la corrélation.
    public async Task<Withdrawal?> GetByProviderRefAsync(string providerRef, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .FirstOrDefaultAsync(w => w.ProviderRef == providerRef, cancellationToken);
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

    /// <summary>
    /// Le grand livre a-t-il déjà une écriture pour cette référence ?
    ///
    /// ⚠️ On interroge le CHANGE TRACKER autant que la base (pas d'AsNoTracking, et
    /// `Local` d'abord) : dans une même transaction, les écritures précédentes ne sont
    /// pas encore commitées. Sans cela, deux handlers du même message se croiraient
    /// tous deux « premiers ».
    /// </summary>
    public async Task<bool> ExistsForReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken cancellationToken = default)
    {
        var pending = _dbContext.WalletTransactions.Local
            .Any(t => t.ReferenceType == referenceType && t.ReferenceId == referenceId);

        if (pending)
        {
            return true;
        }

        return await _dbContext.WalletTransactions
            .AnyAsync(t => t.ReferenceType == referenceType && t.ReferenceId == referenceId, cancellationToken);
    }
}
