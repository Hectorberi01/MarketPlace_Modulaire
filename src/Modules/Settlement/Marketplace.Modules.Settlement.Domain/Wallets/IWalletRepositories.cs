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
    /// <summary>Charge une demande de retrait par son identifiant (validation admin).</summary>
    Task<Withdrawal?> GetByIdAsync(WithdrawalId id, CancellationToken cancellationToken = default);
    /// <summary>Liste les retraits d'un statut donné, tous vendeurs confondus (file admin).</summary>
    Task<IReadOnlyList<Withdrawal>> ListByStatusAsync(WithdrawalStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retraits « en cours » (versement demandé au PSP, non confirmé), avec SUIVI EF :
    /// la réconciliation les mute (clôture ou échec + remboursement), il ne faut donc
    /// pas de AsNoTracking ici.
    /// </summary>
    Task<IReadOnlyList<Withdrawal>> ListProcessingForReconciliationAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrait porteur d'une référence de dépôt PSP (corrélation d'un webhook payout).
    /// SUIVI EF activé : l'entité est mutée juste après.
    /// </summary>
    Task<Withdrawal?> GetByProviderRefAsync(string providerRef, CancellationToken cancellationToken = default);
}

/// <summary>Grand livre des mouvements de wallet (vendeur et plateforme).</summary>
public interface IWalletTransactionRepository
{
    Task AddAsync(WalletTransaction transaction, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WalletTransaction>> ListByOwnerAsync(Guid ownerId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Une écriture existe-t-elle déjà pour cette référence ? — LE VERROU D'IDEMPOTENCE.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// L'outbox livre AT-LEAST-ONCE : si l'un des handlers d'un événement échoue, le
    /// message est rejoué EN ENTIER — y compris les handlers qui avaient réussi.
    ///
    /// Sans ce garde-fou, un rejeu de « remboursement versé » débitait le vendeur une
    /// SECONDE fois et restituait la commission deux fois. Une perte comptable réelle,
    /// silencieuse, et d'autant plus perverse qu'elle survenait dans le code même qui
    /// devait fiabiliser les remboursements.
    ///
    /// Le grand livre sert ici de REGISTRE : s'il porte déjà une écriture pour ce
    /// remboursement, c'est qu'il a déjà été contre-passé. On ne recommence pas.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    Task<bool> ExistsForReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken cancellationToken = default);
}
