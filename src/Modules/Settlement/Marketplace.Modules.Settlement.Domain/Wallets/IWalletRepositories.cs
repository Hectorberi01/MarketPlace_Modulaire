namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>
/// Accès aux portefeuilles, tous acteurs confondus. Un portefeuille est identifié par
/// le couple (type de propriétaire, identifiant) — c'est ce couple qui est unique en
/// base, pas l'identifiant seul : rien n'interdit qu'un même compte utilisateur soit à
/// la fois acheteur et livreur, et ce sont alors deux portefeuilles distincts.
/// </summary>
public interface IWalletRepository
{
    Task<Wallet?> GetAsync(WalletOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default);

    Task<Wallet?> GetByIdAsync(WalletId id, CancellationToken cancellationToken = default);

    /// <summary>Le portefeuille de la plateforme (propriétaire unique).</summary>
    Task<Wallet?> GetPlatformAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Wallet wallet, CancellationToken cancellationToken = default);
}

/// <summary>Accès aux demandes de retrait, tous acteurs confondus.</summary>
public interface IWithdrawalRepository
{
    Task AddAsync(Withdrawal withdrawal, CancellationToken cancellationToken = default);
    /// <summary>
    /// Retraits d'un acteur donné. Le filtre porte sur le couple (type, identifiant)
    /// plutôt que sur le portefeuille : les écrans listent « les retraits de ce vendeur »
    /// sans avoir à charger son portefeuille au préalable.
    /// </summary>
    Task<IReadOnlyList<Withdrawal>> ListByOwnerAsync(
        WalletOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default);
    /// <summary>Charge une demande de retrait par son identifiant (validation admin).</summary>
    Task<Withdrawal?> GetByIdAsync(WithdrawalId id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Liste les retraits d'un statut donné (file admin). Le filtre par acteur est
    /// facultatif : la console veut tantôt tout voir, tantôt la seule file des vendeurs.
    /// </summary>
    Task<IReadOnlyList<Withdrawal>> ListByStatusAsync(
        WithdrawalStatus status, WalletOwnerType? ownerType = null, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Somme signée des écritures d'un compte (crédits moins débits).
    ///
    /// C'est le contrôle qui donne tout son sens au grand livre : il doit rendre
    /// EXACTEMENT le solde porté par le portefeuille. Tant que les soldes vivaient en
    /// colonnes sur des agrégats distincts, tenus à jour par un autre chemin de code que
    /// les écritures, rien ne le garantissait — et rien ne le vérifiait.
    /// </summary>
    Task<decimal> SumForAccountAsync(
        Guid ownerId, WalletOwnerType ownerType, WalletAccount account, CancellationToken cancellationToken = default);
}
