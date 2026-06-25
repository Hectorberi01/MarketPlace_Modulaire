using System.Text.RegularExpressions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Catalog.Domain.Products;

/// <summary>
/// SKU : référence unique d'une variante, contrat partagé avec Inventory et
/// Pricing (cf. dossier). Value Object normalisé en majuscules.
/// </summary>
public sealed partial class Sku : ValueObject
{
    private Sku(string value) => Value = value;

    public string Value { get; }

    public static Result<Sku> Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("catalog.sku.empty", "Le SKU ne peut pas être vide.");
        }

        var normalized = input.Trim().ToUpperInvariant();

        if (normalized.Length > 64)
        {
            return Error.Validation("catalog.sku.length", "Le SKU doit faire au plus 64 caractères.");
        }

        if (!SkuPattern().IsMatch(normalized))
        {
            return Error.Validation("catalog.sku.format", "Le SKU n'accepte que lettres, chiffres, tirets et underscores.");
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
