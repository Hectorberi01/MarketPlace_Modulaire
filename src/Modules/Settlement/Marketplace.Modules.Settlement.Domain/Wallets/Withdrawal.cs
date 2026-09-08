using Marketplace.BuildingBlocks.Domain.Primitives;

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

    private Withdrawal(
        WithdrawalId id, WalletId walletId, WalletOwnerType ownerType, Guid ownerId,
        decimal amount, string currency)
        : base(id)
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

    public static Withdrawal Create(Wallet wallet, decimal amount)
        => new(WithdrawalId.New(), wallet.Id, wallet.OwnerType, wallet.OwnerId, amount, wallet.Currency);

    /// <summary>
    /// Le versement a été DEMANDÉ au PSP (créé + démarré), ou son issue est indéterminée.
    /// Les fonds restent débités : on ne rembourse pas tant qu'on ne sait pas.
    /// <paramref name="providerRef"/> peut être nul (cas d'un timeout avant d'obtenir
    /// l'identifiant) — le retrait devra alors être tranché manuellement.
    /// </summary>
    public void MarkProcessing(string? providerRef, string? note = null)
    {
        Status = WithdrawalStatus.Processing;
        ProviderRef = providerRef ?? ProviderRef;
        FailureReason = note; // trace de l'anomalie éventuelle (timeout…)
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
