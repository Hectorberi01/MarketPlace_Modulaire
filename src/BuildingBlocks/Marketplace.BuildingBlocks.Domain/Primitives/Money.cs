using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Montant monétaire (Value Object du shared kernel) : un montant et sa devise.
/// Partagé entre Offers, Pricing, Ordering, Payments — c'est un concept ubiquitaire
/// et stable. XOF par défaut sur le marché visé.
/// </summary>
public sealed class Money : ValueObject
{
    public const string DefaultCurrency = "XOF";

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }
    public string Currency { get; }

    public static Result<Money> Create(decimal amount, string currency = DefaultCurrency)
    {
        if (amount < 0m)
        {
            return Error.Validation("money.amount_negative", "Le montant ne peut pas être négatif.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            return Error.Validation("money.currency_invalid", "La devise doit être un code ISO à 3 lettres.");
        }

        return new Money(decimal.Round(amount, 2), currency.Trim().ToUpperInvariant());
    }

    public static Money Zero(string currency = DefaultCurrency) => new(0m, currency);

    public Result<Money> Add(Money other)
    {
        if (other.Currency != Currency)
        {
            return Error.Validation("money.currency_mismatch", "Devises incompatibles.");
        }

        return new Money(Amount + other.Amount, Currency);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
