using System.Text.RegularExpressions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Identity.Domain.Users;

/// <summary>
/// Numéro de téléphone normalisé. Clé majeure sur le marché visé (SMS, mobile
/// money). On garde un format E.164 simplifié : « + » optionnel suivi de 8 à 15
/// chiffres. Value Object.
/// </summary>
public sealed partial class PhoneNumber : ValueObject
{
    private PhoneNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<PhoneNumber> Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("identity.phone.empty", "Le numéro de téléphone est obligatoire.");
        }

        var normalized = Normalize(input);

        if (!PhonePattern().IsMatch(normalized))
        {
            return Error.Validation("identity.phone.invalid", "Numéro de téléphone invalide (8 à 15 chiffres, indicatif optionnel).");
        }

        return new PhoneNumber(normalized);
    }

    private static string Normalize(string input)
    {
        var trimmed = input.Trim();
        var hasPlus = trimmed.StartsWith('+');
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        return hasPlus ? $"+{digits}" : digits;
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^\+?[0-9]{8,15}$")]
    private static partial Regex PhonePattern();
}
