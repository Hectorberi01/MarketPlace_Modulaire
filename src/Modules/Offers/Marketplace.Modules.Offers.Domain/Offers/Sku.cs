using System.Text.RegularExpressions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Offers.Domain.Offers;

/// <summary>
/// SKU : référence unique d'une variante. Value Object local au module (chaque
/// bounded context garde sa propre représentation du contrat partagé).
/// </summary>
public sealed partial class Sku : ValueObject
{
    private Sku(string value) => Value = value;

    public string Value { get; }

    public static Result<Sku> Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("offers.sku.empty", "Le SKU ne peut pas être vide.");
        }

        var normalized = input.Trim().ToUpperInvariant();
        if (normalized.Length > 64 || !SkuPattern().IsMatch(normalized))
        {
            return Error.Validation("offers.sku.invalid", "SKU invalide (lettres, chiffres, tirets, underscores ; max 64).");
        }

        return new Sku(normalized);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z0-9_-]+$")]
    private static partial Regex SkuPattern();
}
