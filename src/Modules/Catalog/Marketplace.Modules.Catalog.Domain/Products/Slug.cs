using System.Text;
using System.Text.RegularExpressions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Catalog.Domain.Products;

/// <summary>
/// Slug SEO unique, dérivé du nom. Value Object : pas d'identité, comparé par sa
/// valeur. La normalisation est centralisée ici (invariant du domaine).
/// </summary>
public sealed partial class Slug : ValueObject
{
    private Slug(string value) => Value = value;

    public string Value { get; }

    public static Result<Slug> Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("catalog.slug.empty", "Le slug ne peut pas être vide.");
        }

        var normalized = Normalize(input);

        if (normalized.Length is 0 or > 200)
        {
            return Error.Validation("catalog.slug.length", "Le slug doit faire entre 1 et 200 caractères.");
        }

        return new Slug(normalized);
    }

    private static string Normalize(string input)
    {
        var lowered = input.Trim().ToLowerInvariant();
        var ascii = RemoveDiacritics(lowered);
        var hyphenated = NonAlphanumeric().Replace(ascii, "-");
        return hyphenated.Trim('-');
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
