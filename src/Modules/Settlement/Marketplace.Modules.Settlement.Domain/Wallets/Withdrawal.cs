using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>
/// Demande de retrait d'un vendeur depuis son solde principal. Exécutée
/// immédiatement via un payout FedaPay Mobile Money : passe à Completed en cas
/// de succès (avec la référence PSP) ou Failed (avec le motif) sinon.
/// </summary>
public sealed class Withdrawal : AggregateRoot<WithdrawalId>
{
    private Withdrawal()
    {
    }

    private Withdrawal(WithdrawalId id, Guid sellerId, decimal amount, string currency)
        : base(id)
    {
        SellerId = sellerId;
        Amount = amount;
        Currency = currency;
        Status = WithdrawalStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid SellerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public WithdrawalStatus Status { get; private set; }
    public string? ProviderRef { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static Withdrawal Create(Guid sellerId, decimal amount, string currency)
        => new(WithdrawalId.New(), sellerId, amount,
            string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant());

    public void Complete(string? providerRef)
    {
        Status = WithdrawalStatus.Completed;
        ProviderRef = providerRef;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Fail(string reason)
    {
        Status = WithdrawalStatus.Failed;
        FailureReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }
}
