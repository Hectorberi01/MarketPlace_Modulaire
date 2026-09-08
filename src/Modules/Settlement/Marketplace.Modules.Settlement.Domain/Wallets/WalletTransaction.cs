using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>
/// Écriture immuable au grand livre : trace chaque mouvement de solde — quel que soit
/// l'acteur — avec son sens, son motif et la référence d'origine (commande, retrait,
/// remboursement). Sert d'historique et de relevé, et de registre d'idempotence.
///
/// Depuis que les soldes vivent dans un agrégat Wallet unique alimenté par un seul
/// chemin de code, ce grand livre n'est plus un enregistrement PARALLÈLE des mouvements
/// mais leur contrepartie exacte : la somme de ses lignes doit rendre le solde, au franc
/// près, pour chaque compte.
/// </summary>
public sealed class WalletTransaction : AggregateRoot<Guid>
{
    private WalletTransaction()
    {
    }

    private WalletTransaction(
        Guid id, Guid ownerId, WalletOwnerType ownerType, WalletAccount account,
        WalletDirection direction, decimal amount, string currency,
        string reason, string? referenceType, Guid? referenceId)
        : base(id)
    {
        OwnerId = ownerId;
        OwnerType = ownerType;
        Account = account;
        Direction = direction;
        Amount = amount;
        Currency = currency;
        Reason = reason;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid OwnerId { get; private set; }
    public WalletOwnerType OwnerType { get; private set; }
    public WalletAccount Account { get; private set; }
    public WalletDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public string Reason { get; private set; } = default!;
    public string? ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Écriture rattachée à un portefeuille. C'est la fabrique à utiliser : elle prend le
    /// propriétaire ET la devise à la source, ce qui rend impossible d'écrire une ligne
    /// dans une devise qui n'est pas celle du portefeuille qu'on vient de modifier.
    /// </summary>
    public static WalletTransaction For(
        Wallet wallet, WalletAccount account, WalletDirection direction, decimal amount,
        string reason, string? referenceType = null, Guid? referenceId = null)
        => new(Guid.NewGuid(), wallet.OwnerId, wallet.OwnerType, account, direction, amount,
            Normalize(wallet.Currency), reason, referenceType, referenceId);

    /// <summary>
    /// Écriture rattachée à un propriétaire nommé explicitement. Réservée aux cas où le
    /// portefeuille n'est pas chargé — la reprise de données, essentiellement.
    /// </summary>
    public static WalletTransaction ForOwner(
        WalletOwnerType ownerType, Guid ownerId, WalletAccount account, WalletDirection direction,
        decimal amount, string currency, string reason, string? referenceType = null, Guid? referenceId = null)
        => new(Guid.NewGuid(), ownerId, ownerType, account, direction, amount,
            Normalize(currency), reason, referenceType, referenceId);

    private static string Normalize(string currency)
        => string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant();
}
