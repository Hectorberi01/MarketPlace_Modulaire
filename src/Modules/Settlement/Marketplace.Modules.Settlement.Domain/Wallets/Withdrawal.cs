using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>
/// Demande de retrait depuis le solde disponible d'un portefeuille. Créée à l'état
/// Requested (fonds retenus) et en attente de validation par l'admin. À la validation,
/// un payout Mobile Money est déclenché : Completed en cas de succès (avec la référence
/// PSP) ou Failed (avec le motif). L'admin peut aussi la refuser (Rejected), auquel cas
/// les fonds sont recrédités.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UN SEUL RETRAIT POUR TROIS ACTEURS.
///
/// Ce retrait ne connaissait que le vendeur. Or le livreur retire sa rémunération et le
/// client sa cagnotte exactement de la même façon : mêmes états, même validation admin,
/// même payout, mêmes cas de panne — un « start » accepté ne veut pas dire « reçu », et
/// une issue indéterminée ne doit jamais donner lieu à un recrédit qui autoriserait un
/// double versement.
///
/// Écrire ce cycle trois fois aurait donné trois occasions de se tromper sur le point le
/// plus délicat du module. Le retrait porte donc un PORTEFEUILLE, pas un vendeur ; qui en
/// est le propriétaire est une propriété du portefeuille, pas du retrait.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class Withdrawal : AggregateRoot<WithdrawalId>
{
    private Withdrawal()
    {
    }

    private Withdrawal(WithdrawalId id, WalletId walletId, WalletOwnerType ownerType, Guid ownerId, decimal amount, string currency) : base(id)
    {
        WalletId = walletId;
        OwnerType = ownerType;
        OwnerId = ownerId;
        Amount = amount;
        Currency = currency;
        Status = WithdrawalStatus.Requested;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Vrai tant que la demande attend la validation de l'admin.</summary>
    public bool IsPendingApproval => Status == WithdrawalStatus.Requested;

    /// <summary>Versement demandé au PSP, en attente de confirmation (réconciliation).</summary>
    public bool IsProcessing => Status == WithdrawalStatus.Processing;

    /// <summary>Date de la demande de versement au PSP (sert au délai d'alerte).</summary>
    public DateTime? SentToPspAtUtc { get; private set; }

    /// <summary>Portefeuille débité, et seule source de vérité du propriétaire.</summary>
    public WalletId WalletId { get; private set; }

    /// <summary>
    /// Recopiés depuis le portefeuille à la création. Redondants avec lui, et assumés :
    /// la file de validation admin et les relevés filtrent par acteur, et une jointure
    /// sur le portefeuille à chaque ligne coûterait plus que ces deux colonnes.
    /// </summary>
    public WalletOwnerType OwnerType { get; private set; }

    public Guid OwnerId { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public WithdrawalStatus Status { get; private set; }
    public string? ProviderRef { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>
    /// Crée une demande de retrait, PLAFONNÉE AU SOLDE DISPONIBLE.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// POURQUOI CE CONTRÔLE EST ICI, ET PAS LAISSÉ AU DÉBIT.
    ///
    /// Le débit d'un portefeuille refuse de passer sous zéro… sauf là où la politique de
    /// l'acteur l'autorise — et elle l'autorise pour le vendeur et le livreur, sur le
    /// compte disponible, précisément celui d'où l'on retire.
    ///
    /// Ce droit au négatif existe pour les CONTRE-PASSATIONS : reprendre son gain à un
    /// vendeur déjà payé pour une vente remboursée doit pouvoir le mettre en dette, sans
    /// quoi la perte resterait à la charge de la plateforme. Mais il n'a jamais été pensé
    /// pour un retrait VOLONTAIRE.
    ///
    /// Résultat, avant ce garde-fou : un vendeur ayant 5 000 au disponible pouvait
    /// demander 500 000. Le débit passait — négatif autorisé — la demande était créée,
    /// et à la validation admin le versement partait pour de bon. Le solde négatif était
    /// la seule trace, découverte après coup, et il n'y a aucun moyen de reprendre de
    /// l'argent versé sur un Mobile Money.
    ///
    /// Le contrôle est donc posé sur la CRÉATION DE LA DEMANDE — l'endroit qui distingue
    /// « on me reprend ce que je dois » de « je demande à sortir de l'argent » — et il
    /// vaut d'un coup pour le vendeur, le client et le livreur.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static Result<Withdrawal> Create(Wallet wallet, decimal amount)
    {
        if (amount <= 0m)
        {
            return Error.Validation("wallet.withdrawal_amount_invalid", "Le montant du retrait doit être positif.");
        }

        var available = wallet.Balance(WalletAccount.Available);
        if (amount > available)
        {
            return Error.Validation(
                "wallet.insufficient_funds",
                $"Solde insuffisant : {available} {wallet.Currency} disponible, {amount} demandé.");
        }

        return new Withdrawal(WithdrawalId.New(), wallet.Id, wallet.OwnerType, wallet.OwnerId, amount, wallet.Currency);
    }

    /// <summary>
    /// Le versement a été DEMANDÉ au PSP (créé + démarré), ou son issue est indéterminée.
    /// Les fonds restent débités : on ne rembourse pas tant qu'on ne sait pas.
    /// <paramref name="providerRef"/> peut être nul (cas d'un timeout avant d'obtenir
    /// l'identifiant) — le retrait devra alors être tranché manuellement.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// <paramref name="incident"/> DÉCRIT UN INCIDENT, JAMAIS UNE ÉTAPE.
    ///
    /// Ce paramètre alimente <see cref="FailureReason"/>, que le BFF admin expose sous
    /// le nom « anomaly » et que la console lit comme le signal d'un problème. Il a
    /// longtemps servi de journal : l'appelant y écrivait « approbation en cours »
    /// avant même de joindre le prestataire, et le tableau de bord affichait alors
    /// « le vendeur est débité sans avoir reçu son argent » pendant le déroulement
    /// NORMAL d'une validation. Un faux positif répété sur la seule alerte rouge d'un
    /// écran apprend à l'ignorer — c'est son vrai coût.
    ///
    /// La règle, donc : ne renseigner ce paramètre que lorsque quelque chose s'est
    /// mal passé. L'avancement, lui, se lit sur <see cref="Status"/>,
    /// <see cref="SentToPspAtUtc"/> et la présence d'une référence prestataire.
    ///
    /// L'omettre EFFACE l'incident précédent, ce qui est voulu : un second passage qui
    /// aboutit doit lever le drapeau posé par le premier.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public void MarkProcessing(string? providerRef, string? incident = null)
    {
        Status = WithdrawalStatus.Processing;
        ProviderRef = providerRef ?? ProviderRef;
        FailureReason = incident;
        SentToPspAtUtc ??= DateTime.UtcNow;
    }

    /// <summary>
    /// Versement CONFIRMÉ par le PSP (statut « sent »). C'est le seul chemin vers
    /// Completed : on ne clôture jamais sur une simple acceptation de la demande.
    /// </summary>
    public void Complete(string? providerRef)
    {
        Status = WithdrawalStatus.Completed;
        ProviderRef = providerRef ?? ProviderRef;
        FailureReason = null;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Fail(string reason)
    {
        Status = WithdrawalStatus.Failed;
        FailureReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Refus par l'admin (les fonds sont recrédités par le handler).</summary>
    public void Reject(string reason)
    {
        Status = WithdrawalStatus.Rejected;
        FailureReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }
}
