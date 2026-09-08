using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Infrastructure.Persistence;

internal sealed class WalletRepository : IWalletRepository
{
    private readonly SettlementDbContext _dbContext;

    public WalletRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    // SUIVI EF ACTIVÉ, ET LES SOLDES CHARGÉS AVEC. Un portefeuille n'est jamais lu pour
    // être seulement regardé : il est lu pour être modifié, et modifier un solde exige
    // que la collection d'entités enfants soit là. Un Include oublié ici donnerait un
    // agrégat aux comptes vides, donc des crédits silencieusement perdus.
    public async Task<Wallet?> GetAsync(
        WalletOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
        => await _dbContext.Wallets
            .Include(w => w.Accounts)
            .FirstOrDefaultAsync(w => w.OwnerType == ownerType && w.OwnerId == ownerId, cancellationToken);

    public async Task<Wallet?> GetByIdAsync(WalletId id, CancellationToken cancellationToken = default)
        => await _dbContext.Wallets
            .Include(w => w.Accounts)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public Task<Wallet?> GetPlatformAsync(CancellationToken cancellationToken = default)
        => GetAsync(WalletOwnerType.Platform, Wallet.PlatformOwnerId, cancellationToken);

    public async Task AddAsync(Wallet wallet, CancellationToken cancellationToken = default)
        => await _dbContext.Wallets.AddAsync(wallet, cancellationToken);
}

internal sealed class WithdrawalRepository : IWithdrawalRepository
{
    private readonly SettlementDbContext _dbContext;

    public WithdrawalRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Withdrawal withdrawal, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals.AddAsync(withdrawal, cancellationToken);

    public async Task<IReadOnlyList<Withdrawal>> ListByOwnerAsync(
        WalletOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .AsNoTracking()
            .Where(w => w.OwnerType == ownerType && w.OwnerId == ownerId)
            .OrderByDescending(w => w.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    // Suivi activé : l'entité est mutée puis persistée (validation/refus admin).
    public async Task<Withdrawal?> GetByIdAsync(WithdrawalId id, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Withdrawal>> ListByStatusAsync(
        WithdrawalStatus status, WalletOwnerType? ownerType = null, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .AsNoTracking()
            .Where(w => w.Status == status && (ownerType == null || w.OwnerType == ownerType))
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

internal sealed class CustomerRefundRepository : ICustomerRefundRepository
{
    private readonly SettlementDbContext _dbContext;

    public CustomerRefundRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(CustomerRefund refund, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds.AddAsync(refund, cancellationToken);

    // Suivi activé : l'entité est mutée puis persistée (réconciliation).
    public async Task<CustomerRefund?> GetByIdAsync(CustomerRefundId id, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<decimal> SumActiveForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds
            .Where(r => r.OrderId == orderId && r.Status != CustomerRefundStatus.Failed)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

    // SUIVI activé (pas de AsNoTracking) : la réconciliation mute ces entités.
    public async Task<IReadOnlyList<CustomerRefund>> ListProcessingAsync(CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds
            .Where(r => r.Status == CustomerRefundStatus.Processing)
            .OrderBy(r => r.CreatedAtUtc)
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

    /// <summary>
    /// Somme signée des écritures d'un compte. Calculée EN BASE, pas en mémoire : un
    /// portefeuille actif accumule des milliers de lignes, et ce contrôle a vocation à
    /// tourner sur l'ensemble des portefeuilles.
    /// </summary>
    public async Task<decimal> SumForAccountAsync(
        Guid ownerId, WalletOwnerType ownerType, WalletAccount account, CancellationToken cancellationToken = default)
    {
        var rows = _dbContext.WalletTransactions
            .Where(t => t.OwnerId == ownerId && t.OwnerType == ownerType && t.Account == account);

        var credits = await rows.Where(t => t.Direction == WalletDirection.Credit)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var debits = await rows.Where(t => t.Direction == WalletDirection.Debit)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        return credits - debits;
    }
}
