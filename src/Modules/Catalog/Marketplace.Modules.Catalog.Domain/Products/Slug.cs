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

    /// <summary>
    /// Replie les caractères accentués sur leur équivalent ASCII, SANS dépendre d'ICU.
    ///
    /// ────────────────────────────────────────────────────────────────────────────
    /// L'implémentation précédente reposait sur <c>Normalize(NormalizationForm.FormD)</c>
    /// puis sur la suppression des marques non-espaçantes. C'est la méthode canonique,
    /// enseignée partout — et elle était SILENCIEUSEMENT INOPÉRANTE ici.
    ///
    /// La raison tient en une ligne de <c>Directory.Build.props</c> :
    ///
    ///     &lt;InvariantGlobalization&gt;true&lt;/InvariantGlobalization&gt;
    ///
    /// Dans ce mode, .NET n'embarque pas ICU, et <c>String.Normalize</c> devient un
    /// NO-OP : il rend la chaîne telle quelle, sans lever. Le « é » ne se décomposait
    /// donc jamais en « e » + accent ; il restait un caractère unique, que la regex
    /// <c>[^a-z0-9]+</c> traitait ensuite comme un séparateur.
    ///
    /// Résultat, en production comme en test :
    ///     « Téléphone Samsung »  →  « t-l-phone-samsung »
    ///     « Sac à main cuir »    →  « sac-main-cuir »
    ///     « Électronique »       →  « lectronique »
    ///
    /// Et un slug part dans l'URL. Il est indexé, partagé, mis en favori. Une fois
    /// publié, on ne le corrige plus sans casser des liens.
    ///
    /// D'où une table explicite. Elle est verbeuse, mais elle a trois vertus que la
    /// voie « élégante » n'avait pas : elle ne dépend d'aucune bibliothèque système,
    /// elle donne le MÊME résultat sur le poste du développeur et dans le conteneur,
    /// et elle est lisible — on voit ce qui devient quoi.
    /// ────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static string RemoveDiacritics(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            // Chemin rapide : l'immense majorité des caractères est déjà en ASCII.
            if (c < 128)
            {
                builder.Append(c);
                continue;
            }

            builder.Append(Fold(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Latin-1 Supplement + Latin Extended-A, en minuscules (l'appelant a déjà
    /// abaissé la casse). Un caractère non répertorié est rendu tel quel : la regex
    /// suivante le transformera en séparateur, ce qui reste le comportement sûr pour
    /// un alphabet qu'on ne sait pas translittérer (arabe, cyrillique, CJK).
    /// </summary>
    private static string Fold(char c) => c switch
    {
        'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' or 'ā' or 'ă' or 'ą' => "a",
        'ç' or 'ć' or 'ĉ' or 'ċ' or 'č' => "c",
        'ð' or 'ď' or 'đ' => "d",
        'è' or 'é' or 'ê' or 'ë' or 'ē' or 'ĕ' or 'ė' or 'ę' or 'ě' => "e",
        'ĝ' or 'ğ' or 'ġ' or 'ģ' => "g",
        'ĥ' or 'ħ' => "h",
        'ì' or 'í' or 'î' or 'ï' or 'ĩ' or 'ī' or 'ĭ' or 'į' or 'ı' => "i",
        'ĵ' => "j",
        'ķ' => "k",
        'ĺ' or 'ļ' or 'ľ' or 'ŀ' or 'ł' => "l",
        'ñ' or 'ń' or 'ņ' or 'ň' or 'ŉ' => "n",
        'ò' or 'ó' or 'ô' or 'õ' or 'ö' or 'ø' or 'ō' or 'ŏ' or 'ő' => "o",
        'ŕ' or 'ŗ' or 'ř' => "r",
        'ś' or 'ŝ' or 'ş' or 'š' => "s",
        'ţ' or 'ť' or 'ŧ' => "t",
        'ù' or 'ú' or 'û' or 'ü' or 'ũ' or 'ū' or 'ŭ' or 'ů' or 'ű' or 'ų' => "u",
        'ŵ' => "w",
        'ý' or 'ÿ' or 'ŷ' => "y",
        'ź' or 'ż' or 'ž' => "z",

        // Ligatures : elles valent DEUX lettres, pas une. « Cœur » donne « coeur »,
        // et non « cur ».
        'æ' => "ae",
        'œ' => "oe",
        'ß' => "ss",

        _ => c.ToString()
    };

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
