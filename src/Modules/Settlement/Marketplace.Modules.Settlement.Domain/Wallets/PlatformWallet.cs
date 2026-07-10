using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>
/// Portefeuille de la plateforme (admin). Agrégat singleton identifié par
/// <see cref="SingletonId"/>. Deux soldes distincts :
///  • <see cref="CommissionBalance"/> : commissions encaissées sur chaque vente.
///  • <see cref="ProviderFeeBalance"/> : frais provider encaissés sur chaque vente.
///  • <see cref="ShippingBalance"/> : frais de livraison encaissés par la plateforme.
/// </summary>
public sealed class PlatformWallet : AggregateRoot<Guid>
{
    /// <summary>Identifiant fixe du portefeuille plateforme unique.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-0000000000A1");

    private PlatformWallet()
    {
    }

    private PlatformWallet(Guid id, string currency)
        : base(id)
    {
        Currency = currency;
        CommissionBalance = 0m;
        ProviderFeeBalance = 0m;
        ShippingBalance = 0m;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public string Currency { get; private set; } = default!;
    public decimal CommissionBalance { get; private set; }
    public decimal ProviderFeeBalance { get; private set; }
    public decimal ShippingBalance { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static PlatformWallet Create(string currency)
        => new(SingletonId, string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant());

    public void CreditCommission(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        CommissionBalance += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void CreditProviderFee(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        ProviderFeeBalance += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void CreditShipping(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        ShippingBalance += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
