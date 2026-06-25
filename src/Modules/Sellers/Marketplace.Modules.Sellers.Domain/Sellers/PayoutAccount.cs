using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Sellers.Domain.Sellers;

/// <summary>
/// Coordonnées de reversement d'un vendeur (mobile money ou compte bancaire).
/// Value Object : on n'accepte que des comptes complets et valides.
/// </summary>
public sealed class PayoutAccount : ValueObject
{
    private PayoutAccount(PayoutProvider provider, string accountNumber, string accountName)
    {
        Provider = provider;
        AccountNumber = accountNumber;
        AccountName = accountName;
    }

    public PayoutProvider Provider { get; }
    public string AccountNumber { get; }
    public string AccountName { get; }

    public static Result<PayoutAccount> Create(PayoutProvider provider, string accountNumber, string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            return Error.Validation("sellers.payout.account_number_required", "Le numéro de compte est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(accountName))
        {
            return Error.Validation("sellers.payout.account_name_required", "Le nom du titulaire est obligatoire.");
        }

        var number = accountNumber.Trim();
        if (number.Length > 64)
        {
            return Error.Validation("sellers.payout.account_number_invalid", "Numéro de compte invalide.");
        }

        return new PayoutAccount(provider, number, accountName.Trim());
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Provider;
        yield return AccountNumber;
        yield return AccountName;
    }
}
