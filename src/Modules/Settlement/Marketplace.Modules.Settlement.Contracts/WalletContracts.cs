namespace Marketplace.Modules.Settlement.Contracts;

/// <summary>Soldes du portefeuille d'un vendeur.</summary>
public sealed record SellerWalletView(
    Guid SellerId,
    decimal PendingBalance,
    decimal AvailableBalance,
    // Somme des retraits EN COURS DE ROUTE : demandés (attente de validation admin) ET
    // en cours de versement chez le PSP. Dans les deux cas les fonds sont déjà retenus,
    // retirés du solde principal. Ne compter que les premiers ferait « disparaître »
    // l'argent de l'écran du vendeur entre la validation et la confirmation du PSP.
    decimal PendingWithdrawal,
    string Currency);

/// <summary>Demande de retrait en attente de validation (vue admin, enrichie vendeur).</summary>
public sealed record PendingWithdrawalView(
    Guid Id,
    Guid SellerId,
    string SellerName,
    decimal Amount,
    string Currency,
    string? PayoutProvider,
    string? PayoutAccountNumber,
    DateTime CreatedAtUtc);

/// <summary>
/// Retrait dont le versement a été demandé au PSP mais N'EST PAS confirmé (vue admin).
///
/// C'est l'état qui mérite une surveillance humaine : tant qu'il dure, le vendeur est
/// débité sans avoir reçu son argent. <paramref name="Anomaly"/> porte l'incident
/// éventuel (timeout…), et un <paramref name="ProviderRef"/> ABSENT signale le cas le
/// plus grave : versement peut-être parti, mais impossible à réconcilier
/// automatiquement — il faut le retrouver dans le tableau de bord du PSP.
/// </summary>
public sealed record ProcessingWithdrawalView(
    Guid Id,
    Guid SellerId,
    string SellerName,
    decimal Amount,
    string Currency,
    string? ProviderRef,
    string? Anomaly,
    DateTime CreatedAtUtc,
    DateTime? SentToPspAtUtc);

/// <summary>Soldes du portefeuille de la plateforme (admin).</summary>
public sealed record PlatformWalletView(
    decimal CommissionBalance,
    decimal ProviderFeeBalance,
    decimal ShippingBalance,
    decimal RefundsBalance,
    string Currency);

/// <summary>Un remboursement client direct (versement MoMo initié par l'admin).</summary>
public sealed record CustomerRefundView(
    Guid Id,
    Guid OrderId,
    Guid BuyerId,
    decimal Amount,
    string Currency,
    string Reason,
    string Msisdn,
    string Provider,
    string Status,
    string? ProviderRef,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    /// <summary>
    /// « Cash » (versement Mobile Money) ou « Wallet » (crédit de cagnotte). Sans lui,
    /// deux lignes d'un même remboursement seraient indiscernables à l'écran, et un
    /// administrateur croirait qu'un versement a été tenté deux fois.
    /// </summary>
    string Channel = "Cash");

/// <summary>
/// Résultat d'un remboursement direct, RÉPARTI ENTRE SES DEUX CANAUX.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UN REMBOURSEMENT REPART PAR OÙ IL EST VENU.
///
/// Une commande de 13 000 réglée 8 000 en Mobile Money et 5 000 en cagnotte n'a fait
/// entrer que 8 000 en caisse. La rembourser intégralement en espèces sortirait
/// 13 000 de trésorerie, frais de versement compris, pour une dette dont 5 000
/// existaient déjà sous forme de cagnotte. <paramref name="CashAmount"/> est donc
/// plafonné à ce qui a réellement été encaissé, et <paramref name="WalletAmount"/>
/// porte le reste.
///
/// <paramref name="Cash"/> est NUL quand rien ne sort en espèces — commande
/// entièrement réglée en cagnotte, ou part espèces déjà remboursée. Aucun payout
/// n'est alors déclenché, et c'est le cas normal, pas un échec.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record CustomerRefundSplitView(
    Guid OrderId,
    decimal TotalAmount,
    decimal CashAmount,
    decimal WalletAmount,
    string Currency,
    CustomerRefundView? Cash);

/// <summary>Une ligne du grand livre wallet (relevé vendeur / plateforme).</summary>
public sealed record WalletTransactionView(
    Guid Id,
    string Account,
    string Direction,
    decimal Amount,
    string Currency,
    string Reason,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTime CreatedAtUtc);

/// <summary>Une demande de retrait vendeur.</summary>
public sealed record WithdrawalView(
    Guid Id,
    Guid SellerId,
    decimal Amount,
    string Currency,
    string Status,
    string? ProviderRef,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

/// <summary>
/// Un écart constaté entre le solde porté par un portefeuille et ce que le grand livre
/// raconte pour le même compte.
///
/// <paramref name="Drift"/> est signé : positif, le portefeuille affiche PLUS que ce
/// que les écritures justifient (de l'argent apparu de nulle part) ; négatif, il
/// affiche moins (de l'argent débité sans écriture, ou une écriture en trop).
/// </summary>
public sealed record WalletDriftView(
    Guid WalletId,
    string OwnerType,
    Guid OwnerId,
    string Account,
    decimal WalletBalance,
    decimal LedgerBalance,
    decimal Drift);

/// <summary>
/// Le résultat du contrôle de réconciliation : le grand livre et les soldes
/// disent-ils la même chose ?
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE CONTRÔLE EXISTE.
///
/// Un solde est une COLONNE, une valeur que du code met à jour. Le grand livre est
/// une SUITE D'ÉCRITURES, que du code ajoute. Ce sont deux chemins distincts, et
/// rien dans la base ne garantit qu'ils racontent la même histoire : il a suffi
/// d'une libération de fonds qui écrivait le crédit sans écrire le débit
/// correspondant pour que l'écart grandisse à chaque livraison, sans qu'aucun test
/// ni aucun écran ne s'en aperçoive.
///
/// L'invariant est vérifié en test unitaire sur les chemins connus. Ce contrôle-ci
/// le vérifie sur les DONNÉES RÉELLES, y compris celles écrites par du code qu'on
/// n'a pas encore écrit. <paramref name="IsBalanced"/> à faux est une anomalie
/// comptable : rien ne se répare tout seul, il faut aller lire les écritures.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record WalletReconciliationView(
    int WalletsChecked,
    int AccountsChecked,
    decimal TotalAbsoluteDrift,
    IReadOnlyList<WalletDriftView> Drifts,
    DateTime CheckedAtUtc)
{
    public bool IsBalanced => Drifts.Count == 0;
}

/// <summary>
/// La cagnotte d'un client : ce qu'il peut dépenser ou retirer, et ce qui est engagé
/// sur une commande en cours.
/// </summary>
public sealed record CustomerWalletView(
    Guid CustomerId,
    // Disponible : dépensable au paiement, retirable sur Mobile Money.
    decimal AvailableBalance,
    // Engagé sur une commande en cours de paiement, sorti du disponible.
    decimal ReservedBalance,
    // Retraits demandés ou en cours de versement : déjà sortis du disponible.
    decimal PendingWithdrawal,
    string Currency);

/// <summary>
/// Demande de retrait de cagnotte en attente de validation (vue admin).
///
/// Distincte de <see cref="PendingWithdrawalView"/> : un vendeur s'identifie par sa
/// boutique et son compte de versement professionnel, un client par son nom et le
/// moyen Mobile Money qu'il a enregistré pour payer. Les fusionner obligerait à
/// inventer un « nom d'acteur » qui ne veut rien dire pour l'admin qui valide.
/// </summary>
public sealed record PendingCustomerWithdrawalView(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    decimal Amount,
    string Currency,
    string? PayoutProvider,
    string? PayoutAccountNumber,
    DateTime CreatedAtUtc);

/// <summary>
/// Une demande de retrait de cagnotte, vue par le client.
///
/// Jumelle de <see cref="WithdrawalView"/>, dont le champ s'appelle « SellerId » : ce
/// nom est devenu faux le jour où le retrait a cessé d'être réservé aux vendeurs. Le
/// renommer casserait l'application vendeur déjà déployée pour un bénéfice nul de son
/// côté ; exposer « sellerId » à l'acheteur ferait payer la même confusion au
/// développeur mobile. Deux vues, un seul agrégat, aucune ambiguïté chez l'appelant.
/// </summary>
public sealed record CustomerWithdrawalView(
    Guid Id,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string Status,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

/// <summary>
/// Soldes du portefeuille d'un coursier. Même forme que celui d'un vendeur : mêmes
/// comptes, même cycle, même retrait — c'est ce qui a permis de n'écrire le retrait
/// qu'une seule fois.
/// </summary>
public sealed record CourierWalletView(
    Guid CourierId,
    // Réservé à un futur séquestre (livraison contestée). Vaut zéro aujourd'hui : une
    // course livrée est payée au disponible, faute de déclencheur pour l'en sortir.
    decimal PendingBalance,
    decimal AvailableBalance,
    decimal PendingWithdrawal,
    string Currency);

/// <summary>Demande de retrait d'un coursier en attente de validation (vue admin).</summary>
public sealed record PendingCourierWithdrawalView(
    Guid Id,
    Guid CourierId,
    string CourierName,
    // Affiché pour éclairer la décision, pas pour la bloquer : un coursier suspendu
    // conserve le droit de retirer ce qu'il a déjà gagné.
    string CourierStatus,
    decimal Amount,
    string Currency,
    string? PayoutProvider,
    string? PayoutAccountNumber,
    DateTime CreatedAtUtc);
