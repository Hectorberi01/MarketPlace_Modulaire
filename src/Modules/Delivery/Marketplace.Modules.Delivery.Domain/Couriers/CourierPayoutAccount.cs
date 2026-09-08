using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Delivery.Domain.Couriers;

/// <summary>
/// Compte Mobile Money sur lequel le coursier est payé.
///
/// Même forme que le compte de versement d'un vendeur, et c'est voulu : le résolveur
/// de versement de Settlement les traite déjà de la même façon, et un troisième format
/// n'apporterait qu'une troisième occasion de se tromper de champ.
///
/// La liste d'opérateurs N'EST PAS validée ici. Elle vit dans Settlement, aligné sur la
/// table de routage de la passerelle FedaPay ; la dupliquer dans le domaine du coursier
/// la ferait diverger le jour où un opérateur est ajouté d'un seul côté — et un retrait
/// accepté à l'enregistrement échouerait au versement.
/// </summary>
public sealed class CourierPayoutAccount : ValueObject
{
    private CourierPayoutAccount(string provider, string accountNumber, string accountName)
    {
        Provider = provider;
        AccountNumber = accountNumber;
        AccountName = accountName;
    }

    /// <summary>Opérateur Mobile Money (mtnmomo, moovmoney, celtis…).</summary>
    public string Provider { get; private set; } = default!;

    /// <summary>MSISDN, chiffres uniquement.</summary>
    public string AccountNumber { get; private set; } = default!;

    /// <summary>Titulaire, tel que le PSP l'affichera.</summary>
    public string AccountName { get; private set; } = default!;

    public static Result<CourierPayoutAccount> Create(string provider, string accountNumber, string accountName)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return Error.Validation("delivery.courier.payout_provider_required", "L'opérateur de versement est obligatoire.");
        }

        var digits = new string((accountNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 8)
        {
            return Error.Validation("delivery.courier.payout_account_invalid", "Numéro Mobile Money invalide.");
        }

        return new CourierPayoutAccount(
            provider.Trim().ToLowerInvariant(),
            digits,
            string.IsNullOrWhiteSpace(accountName) ? "Coursier" : accountName.Trim());
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Provider;
        yield return AccountNumber;
        yield return AccountName;
    }
}
