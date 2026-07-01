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
/// Available (solde principal retirable). Plateforme : Commission et Shipping
/// (frais de livraison encaissés).
/// </summary>
public enum WalletAccount
{
    Pending = 0,
    Available = 1,
    Commission = 2,
    Shipping = 3
}

/// <summary>Sens d'une écriture (crédit augmente le solde, débit le diminue).</summary>
public enum WalletDirection
{
    Credit = 0,
    Debit = 1
}

/// <summary>Cycle de vie d'un retrait : demandé → payé (Completed) ou échoué.</summary>
public enum WithdrawalStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2
}
