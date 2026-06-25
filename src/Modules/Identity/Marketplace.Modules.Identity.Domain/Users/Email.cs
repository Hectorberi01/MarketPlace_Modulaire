using System.Text.RegularExpressions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Identity.Domain.Users;

/// <summary>
/// Adresse e-mail validée et normalisée (minuscules). Source d'unicité d'un
/// compte. Value Object : comparé par sa valeur.
/// </summary>
public sealed partial class Email : ValueObject
{
    private Email(string value) => Value = value;

    public string Value { get; }

    public static Result<Email> Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("identity.email.empty", "L'e-mail est obligatoire.");
        }

        var normalized = input.Trim().ToLowerInvariant();

        if (normalized.Length > 320 || !EmailPattern().IsMatch(normalized))
        {
            return Error.Validation("identity.email.invalid", "Format d'e-mail invalide.");
        }

        return new Email(normalized);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
