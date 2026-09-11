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

    /// <summary>
    /// Tous les portefeuilles, soldes compris, EN LECTURE SEULE.
    ///
    /// Réservé au contrôle de réconciliation : c'est le seul cas où l'on veut voir
    /// l'ensemble des portefeuilles d'un coup. Aucun écran ne doit s'en servir — la
    /// table grossit avec les utilisateurs, pas avec l'activité.
    /// </summary>
    Task<IReadOnlyList<Wallet>> ListAllAsync(CancellationToken cancellationToken = default);
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

/// <summary>Grand livre des mouvements de wallet — les quatre acteurs.</summary>
public interface IWalletTransactionRepository
{
    Task AddAsync(WalletTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les dernières écritures d'un acteur.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE TYPE D'ACTEUR FAIT PARTIE DE LA CLÉ. IL MANQUAIT.
    ///
    /// Ce filtre ne portait que sur `OwnerId`, alors qu'un portefeuille s'identifie par
    /// le COUPLE (type, identifiant) — c'est d'ailleurs sur ce couple qu'est posé
    /// l'index unique de la table `wallets`, et c'est ce couple que porte chaque
    /// écriture. Rien, dans le modèle, n'interdit qu'un vendeur et un coursier partagent
    /// un même Guid : ce sont deux tables d'origine différentes, sans contrainte
    /// croisée, et un coursier est justement rattaché à un compte utilisateur dont
    /// l'identifiant vit encore ailleurs.
    ///
    /// Le jour où cette collision arrive, un relevé mélange l'argent de deux personnes,
    /// et il le fait en silence — un relevé mélangé ressemble à un relevé. Le coût de la
    /// garde est nul : la colonne est déjà là, déjà indexée.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<IReadOnlyList<WalletTransaction>> ListByOwnerAsync(
        WalletOwnerType ownerType, Guid ownerId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les écritures d'un acteur sur une PÉRIODE, du plus récent au plus ancien.
    ///
    /// Bornes incluses à gauche, exclues à droite — la convention de tout le module, et
    /// la seule qui rende deux périodes contiguës sans recouvrement ni trou.
    ///
    /// La liste est plafonnée : c'est un relevé d'écran, pas un export comptable. Le
    /// TOTAL ne se déduit donc jamais d'elle — voir <see cref="SumInPeriodAsync"/>.
    /// </summary>
    Task<IReadOnlyList<WalletTransaction>> ListInPeriodAsync(
        WalletOwnerType ownerType, Guid ownerId, DateTime fromUtc, DateTime toUtc,
        int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cumul et décompte d'un MOTIF sur une période, agrégés EN BASE.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// POURQUOI PAS UNE SOMME SUR LA LISTE RENVOYÉE CI-DESSUS.
    ///
    /// Parce que cette liste est tronquée. Un coursier qui a fait trois cents courses
    /// dans le mois verrait ses deux cents dernières lignes, et un total calculé dessus
    /// annoncerait deux cents courses — un chiffre faux, plausible, et que rien à
    /// l'écran ne contredirait. Le total et les lignes répondent donc à deux questions
    /// différentes et se lisent par deux chemins différents.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<LedgerPeriodTotal> SumInPeriodAsync(
        WalletOwnerType ownerType, Guid ownerId, string referenceType,
        WalletDirection direction, DateTime fromUtc, DateTime toUtc,
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// La même somme, mais pour TOUS les comptes de TOUS les portefeuilles, en une
    /// requête agrégée.
    ///
    /// Le contrôle de réconciliation compare des milliers de couples (portefeuille,
    /// compte) : les obtenir un par un via <see cref="SumForAccountAsync"/> ferait
    /// autant d'allers-retours SQL, et le contrôle deviendrait trop coûteux pour être
    /// exécuté — donc ne serait jamais exécuté. Un contrôle qu'on n'ose pas lancer ne
    /// protège de rien.
    /// </summary>
    Task<IReadOnlyList<LedgerAccountSum>> SumAllAccountsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Solde d'un compte tel que le GRAND LIVRE le raconte : crédits moins débits, pour un
/// couple (propriétaire, compte). À confronter au solde porté par le portefeuille.
/// </summary>
public sealed record LedgerAccountSum(
    Guid OwnerId, WalletOwnerType OwnerType, WalletAccount Account, decimal Sum);

/// <summary>
/// Ce qu'un motif a produit sur une période : combien, et en combien de fois.
///
/// Le DÉCOMPTE voyage avec le montant parce qu'il répond à une question que le montant
/// seul ne tranche pas — « quinze mille francs » ne dit pas si c'est une grosse course
/// ou vingt petites, et c'est cette seconde lecture qui fait juger une rémunération.
/// </summary>
public sealed record LedgerPeriodTotal(decimal Amount, int Count);
