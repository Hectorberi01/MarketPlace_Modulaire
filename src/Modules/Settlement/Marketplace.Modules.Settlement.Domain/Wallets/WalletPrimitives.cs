namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>Identité forte d'un portefeuille vendeur.</summary>
public readonly record struct SellerWalletId(Guid Value)
{
    public static SellerWalletId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Identité forte d'un retrait vendeur.</summary>
public readonly record struct WithdrawalId(Guid Value)
{
    public static WithdrawalId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Type de propriétaire d'une écriture au grand livre du wallet.</summary>
public enum WalletOwnerType
{
    Seller = 0,
    Platform = 1
}

/// <summary>
/// Sous-compte visé par une écriture. Vendeur : Pending (solde à venir) et
/// Available (solde principal retirable). Plateforme : Commission, Provider
/// (frais provider encaissés) et Shipping (frais de livraison encaissés).
/// </summary>
public enum WalletAccount
{
    Pending = 0,
    Available = 1,
    Commission = 2,
    Shipping = 3,
    Provider = 4
}

/// <summary>Sens d'une écriture (crédit augmente le solde, débit le diminue).</summary>
public enum WalletDirection
{
    Credit = 0,
    Debit = 1
}

/// <summary>
/// Cycle de vie d'un retrait :
///  • Requested : demandé par le vendeur, en attente de validation admin (fonds retenus).
///  • Completed : validé par l'admin et payé (payout FedaPay réussi).
///  • Failed    : validé mais échec du payout (fonds recrédités).
///  • Rejected  : refusé par l'admin (fonds recrédités).
/// (Pending : ancien statut des retraits auto — conservé pour les données historiques.)
/// </summary>
public enum WithdrawalStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Requested = 3,
    Rejected = 4,

    /// <summary>
    /// Versement DEMANDÉ au PSP mais pas encore confirmé. État essentiel : un « start »
    /// FedaPay accepté ne veut dire que « started », pas « reçu ». Les fonds restent
    /// débités du vendeur ; la réconciliation clôturera en Completed (statut « sent »)
    /// ou en Failed avec remboursement (statut « failed »).
    /// Couvre aussi les issues INDÉTERMINÉES (timeout) : on ne rembourse pas, car
    /// l'argent est peut-être parti — rembourser autoriserait un double versement.
    /// </summary>
    Processing = 5
}
