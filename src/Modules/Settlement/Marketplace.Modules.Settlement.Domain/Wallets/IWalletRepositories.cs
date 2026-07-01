namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>Accès au portefeuille vendeur (un par vendeur, créé à la volée).</summary>
public interface ISellerWalletRepository
{
    Task<SellerWallet?> GetBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);
    Task AddAsync(SellerWallet wallet, CancellationToken cancellationToken = default);
}

/// <summary>Accès au portefeuille plateforme (singleton).</summary>
public interface IPlatformWalletRepository
{
    Task<PlatformWallet?> GetAsync(CancellationToken cancellationToken = default);
    Task AddAsync(PlatformWallet wallet, CancellationToken cancellationToken = default);
}

/// <summary>Accès aux demandes de retrait vendeur.</summary>
public interface IWithdrawalRepository
{
    Task AddAsync(Withdrawal withdrawal, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Withdrawal>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);
}

/// <summary>Grand livre des mouvements de wallet (vendeur et plateforme).</summary>
public interface IWalletTransactionRepository
{
    Task AddAsync(WalletTransaction transaction, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WalletTransaction>> ListByOwnerAsync(Guid ownerId, int take, CancellationToken cancellationToken = default);
}
