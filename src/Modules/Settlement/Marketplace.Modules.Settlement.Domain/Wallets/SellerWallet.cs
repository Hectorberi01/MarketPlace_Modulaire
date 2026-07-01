using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>
/// Portefeuille d'un vendeur. Deux soldes :
///  • <see cref="PendingBalance"/> (« solde à venir ») : gains crédités à la
///    confirmation d'une commande, encore en attente de livraison.
///  • <see cref="AvailableBalance"/> (« solde principal ») : gains libérés à la
///    livraison, retirables par le vendeur.
/// Tous les montants sont NETS (commission déjà déduite).
/// </summary>
public sealed class SellerWallet : AggregateRoot<SellerWalletId>
{
    private SellerWallet()
    {
    }

    private SellerWallet(SellerWalletId id, Guid sellerId, string currency)
        : base(id)
    {
        SellerId = sellerId;
        Currency = currency;
        PendingBalance = 0m;
        AvailableBalance = 0m;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid SellerId { get; private set; }
    public string Currency { get; private set; } = default!;
    public decimal PendingBalance { get; private set; }
    public decimal AvailableBalance { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static SellerWallet Create(Guid sellerId, string currency)
        => new(SellerWalletId.New(), sellerId, Normalize(currency));

    /// <summary>Crédite le solde à venir (gain net d'une commande confirmée).</summary>
    public void CreditPending(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        PendingBalance += amount;
        Touch();
    }

    /// <summary>
    /// Déplace un montant du solde à venir vers le solde principal (livraison
    /// confirmée). Borné au solde à venir disponible pour éviter tout négatif.
    /// </summary>
    public void ReleaseToAvailable(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        var moved = Math.Min(amount, PendingBalance);
        if (moved <= 0m)
        {
            return;
        }

        PendingBalance -= moved;
        AvailableBalance += moved;
        Touch();
    }

    /// <summary>
    /// Débite le solde principal pour un retrait. Échoue si le montant est
    /// invalide ou dépasse le solde disponible.
    /// </summary>
    public Result Withdraw(decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation("wallet.amount_invalid", "Le montant du retrait doit être positif."));
        }

        if (amount > AvailableBalance)
        {
            return Result.Failure(Error.Validation("wallet.insufficient_funds", "Solde principal insuffisant pour ce retrait."));
        }

        AvailableBalance -= amount;
        Touch();
        return Result.Success();
    }

    /// <summary>Recrédite le solde principal (annulation d'un retrait échoué).</summary>
    public void CreditAvailable(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        AvailableBalance += amount;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    private static string Normalize(string currency)
        => string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant();
}
