using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>
/// Écriture immuable au grand livre du wallet : trace chaque mouvement de solde
/// (vendeur ou plateforme) avec son sens, son motif et la référence d'origine
/// (commande, retrait). Sert d'historique/relevé côté vendeur et admin.
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

    public static WalletTransaction ForSeller(
        Guid sellerId, WalletAccount account, WalletDirection direction, decimal amount,
        string currency, string reason, string? referenceType = null, Guid? referenceId = null)
        => new(Guid.NewGuid(), sellerId, WalletOwnerType.Seller, account, direction, amount,
            Normalize(currency), reason, referenceType, referenceId);

    public static WalletTransaction ForPlatform(
        WalletAccount account, WalletDirection direction, decimal amount,
        string currency, string reason, string? referenceType = null, Guid? referenceId = null)
        => new(Guid.NewGuid(), PlatformWallet.SingletonId, WalletOwnerType.Platform, account, direction, amount,
            Normalize(currency), reason, referenceType, referenceId);

    private static string Normalize(string currency)
        => string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant();
}
