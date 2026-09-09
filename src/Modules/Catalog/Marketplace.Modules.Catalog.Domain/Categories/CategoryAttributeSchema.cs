using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Catalog.Domain.Categories;

/// <summary>Type d'un attribut de catégorie. Décide de la manière dont la valeur est contrôlée.</summary>
public enum CategoryAttributeType
{
    /// <summary>Texte libre, éventuellement borné en longueur.</summary>
    Text = 0,

    /// <summary>Nombre décimal, éventuellement borné.</summary>
    Number = 1,

    /// <summary>« true » ou « false ».</summary>
    Boolean = 2,

    /// <summary>Valeur choisie dans une liste fermée.</summary>
    Enum = 3
}

/// <summary>
/// Définition d'UN attribut attendu par une catégorie.
/// </summary>
/// <param name="Key">
/// Clé technique, telle qu'elle apparaît dans le dictionnaire d'attributs du produit.
/// C'est elle qui doit rester stable : le libellé peut être retraduit, la clé non.
/// </param>
/// <param name="Label">Libellé affiché au vendeur dans le formulaire produit.</param>
/// <param name="Type">Type de la valeur, qui décide du contrôle appliqué.</param>
/// <param name="Required">Un produit de cette catégorie doit-il porter cet attribut ?</param>
/// <param name="Values">Valeurs admises, pour un attribut de type <see cref="CategoryAttributeType.Enum"/>.</param>
/// <param name="Unit">Unité affichée à côté du champ (« g », « cm », « W »). Purement indicatif.</param>
/// <param name="Min">Borne inférieure incluse (nombre), ou longueur minimale (texte).</param>
/// <param name="Max">Borne supérieure incluse (nombre), ou longueur maximale (texte).</param>
public sealed record CategoryAttributeDefinition(
    string Key,
    string Label,
    CategoryAttributeType Type,
    bool Required,
    IReadOnlyList<string> Values,
    string? Unit,
    decimal? Min,
    decimal? Max);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE SCHÉMA D'ATTRIBUTS D'UNE CATÉGORIE, ENFIN LU.
///
/// `Category.AttributeSchema` existait depuis l'origine : une colonne `jsonb`, une
/// entrée dans les formulaires d'administration, un champ transporté par les commandes
/// de création et de modification. Quinze occurrences dans le dépôt, TOUTES en
/// écriture ou en transport. Aucune lecture. Aucune validation. Le champ n'était même
/// pas vérifié comme JSON avant d'atteindre la colonne — une saisie fautive donnait un
/// 500 opaque.
///
/// Les attributs dynamiques des produits n'étaient donc contraints par rien : deux
/// vendeurs de la même catégorie livraient des clés incompatibles (« couleur »,
/// « Couleur », « color »), et les facettes de recherche ne pouvaient s'appuyer sur
/// aucune garantie.
///
/// CE TYPE REND LE SCHÉMA EXÉCUTOIRE : il le lit, le valide à l'écriture, et contrôle
/// les attributs de chaque produit contre celui de SA catégorie.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// POURQUOI PAS JSON SCHEMA.
///
/// JSON Schema saurait tout exprimer, au prix d'une dépendance, d'un vocabulaire que
/// l'administrateur devrait apprendre, et de messages d'erreur écrits pour des
/// développeurs (« #/properties/couleur: does not match pattern »). Le besoin ici est
/// étroit et connu : une liste de champs typés, requis ou non, avec des valeurs
/// admises. Un format explicite tient en trente lignes, se saisit dans un formulaire,
/// et produit des messages qu'un vendeur comprend.
///
/// FORMAT ATTENDU (jsonb de la colonne `attribute_schema`) :
///
///   {
///     "allowUnknown": false,
///     "attributes": [
///       { "key": "couleur",  "label": "Couleur", "type": "enum",
///         "required": true,  "values": ["rouge", "bleu", "noir"] },
///       { "key": "poids",    "label": "Poids",   "type": "number",
///         "required": false, "unit": "g", "min": 0, "max": 50000 },
///       { "key": "bio",      "label": "Issu de l'agriculture biologique", "type": "boolean" }
///     ]
///   }
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// CHAQUE CATÉGORIE DÉFINIT LE SIEN — IL N'Y A PAS D'HÉRITAGE.
///
/// Une catégorie fille ne reprend pas les attributs de son parent. C'est un choix, et
/// il se défend : un schéma hérité ne se lit nulle part en entier, il faut remonter
/// l'arbre pour savoir ce qu'un produit doit porter, et le jour où un attribut est
/// ajouté haut dans l'arbre, TOUS les produits des branches en dessous deviennent
/// invalides d'un coup, sans qu'aucun vendeur n'ait rien fait.
///
/// Le prix de ce choix est la répétition : « couleur » sera redéfini dans plusieurs
/// catégories. C'est le bon prix — un schéma qu'on lit d'un seul endroit vaut mieux
/// qu'un schéma qu'on déduit.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// LE SCHÉMA VIDE N'INTERDIT RIEN.
///
/// Toutes les catégories existantes portent `{}`. Un schéma vide laisse donc passer
/// n'importe quels attributs, et aucun produit déjà en base ne devient invalide le
/// jour où ce contrôle entre en service. C'est en renseignant le schéma d'une
/// catégorie qu'un administrateur décide d'y appliquer une contrainte.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CategoryAttributeSchema
{
    /// <summary>Nombre maximal d'attributs dans un schéma. Au-delà, ce n'est plus un formulaire.</summary>
    public const int MaxAttributes = 50;

    /// <summary>Nombre maximal de valeurs dans une liste fermée.</summary>
    public const int MaxEnumValues = 200;

    /// <summary>Longueur maximale d'une valeur d'attribut de produit, contrôle par défaut.</summary>
    public const int MaxValueLength = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private CategoryAttributeSchema(IReadOnlyList<CategoryAttributeDefinition> attributes, bool allowUnknown)
    {
        Attributes = attributes;
        AllowUnknownAttributes = allowUnknown;
    }

    public IReadOnlyList<CategoryAttributeDefinition> Attributes { get; }

    /// <summary>
    /// Un produit peut-il porter des attributs absents du schéma ? Vrai par défaut :
    /// un schéma décrit ce qu'on ATTEND, pas nécessairement tout ce qui est permis.
    /// Le passer à faux ferme la catégorie à un jeu d'attributs exact.
    /// </summary>
    public bool AllowUnknownAttributes { get; }

    /// <summary>Schéma sans aucune contrainte : c'est celui de toutes les catégories qui n'en déclarent pas.</summary>
    public static CategoryAttributeSchema Empty { get; } =
        new(Array.Empty<CategoryAttributeDefinition>(), allowUnknown: true);

    /// <summary>Vrai si le schéma ne contraint rien.</summary>
    public bool IsEmpty => Attributes.Count == 0 && AllowUnknownAttributes;

    /// <summary>
    /// Lit le schéma stocké. Un JSON vide, « {} » ou nul donne <see cref="Empty"/>.
    ///
    /// TOUTE ERREUR EST RENVOYÉE, JAMAIS LEVÉE. Ce texte vient d'une saisie
    /// d'administration : un JSON mal formé est une faute de frappe ordinaire, pas un
    /// incident serveur. Il doit produire un message qui dit quoi corriger, à
    /// l'enregistrement du schéma — et non un 500 le jour où un vendeur crée un
    /// produit dans cette catégorie.
    /// </summary>
    public static Result<CategoryAttributeSchema> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        var trimmed = json.Trim();
        if (trimmed is "{}" or "[]" or "null")
        {
            return Empty;
        }

        SchemaDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SchemaDocument>(trimmed, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Error.Validation(
                "catalog.category.schema_invalid_json",
                $"Le schéma d'attributs n'est pas un JSON valide : {ex.Message}");
        }

        if (document?.Attributes is null || document.Attributes.Count == 0)
        {
            // Un objet JSON valide mais sans attribut ne contraint rien. On respecte
            // tout de même `allowUnknown` : « aucun attribut, et aucun autre non
            // plus » est une déclaration légitime — celle d'une catégorie qui
            // n'accepte aucun attribut du tout.
            return document?.AllowUnknown == false
                ? new CategoryAttributeSchema(Array.Empty<CategoryAttributeDefinition>(), allowUnknown: false)
                : Empty;
        }

        if (document.Attributes.Count > MaxAttributes)
        {
            return Error.Validation(
                "catalog.category.schema_too_large",
                $"Un schéma ne peut pas déclarer plus de {MaxAttributes} attributs.");
        }

        var definitions = new List<CategoryAttributeDefinition>(document.Attributes.Count);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in document.Attributes)
        {
            var parsed = ParseDefinition(raw, seenKeys);
            if (parsed.IsFailure)
            {
                return Result.Failure<CategoryAttributeSchema>(parsed.Error);
            }

            definitions.Add(parsed.Value);
        }

        return new CategoryAttributeSchema(definitions, document.AllowUnknown ?? true);
    }

    private static Result<CategoryAttributeDefinition> ParseDefinition(RawAttribute raw, HashSet<string> seenKeys)
    {
        var key = raw.Key?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return Error.Validation("catalog.category.schema_key_required", "Chaque attribut du schéma doit avoir une clé.");
        }

        // La clé identifie l'attribut d'un produit à l'autre : deux clés qui ne
        // diffèrent que par la casse produiraient deux facettes pour une même notion.
        if (!seenKeys.Add(key))
        {
            return Error.Validation(
                "catalog.category.schema_key_duplicate",
                $"La clé « {key} » est déclarée deux fois dans le schéma.");
        }

        var type = ParseType(raw.Type);
        if (type is null)
        {
            return Error.Validation(
                "catalog.category.schema_type_invalid",
                $"Type inconnu « {raw.Type} » pour l'attribut « {key} » (text, number, boolean ou enum).");
        }

        var values = raw.Values?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList()
                     ?? new List<string>();

        if (type == CategoryAttributeType.Enum)
        {
            if (values.Count == 0)
            {
                return Error.Validation(
                    "catalog.category.schema_values_required",
                    $"L'attribut « {key} » est de type enum : il doit lister ses valeurs admises.");
            }

            if (values.Count > MaxEnumValues)
            {
                return Error.Validation(
                    "catalog.category.schema_values_too_many",
                    $"L'attribut « {key} » déclare plus de {MaxEnumValues} valeurs.");
            }
        }

        if (raw.Min is { } min && raw.Max is { } max && min > max)
        {
            return Error.Validation(
                "catalog.category.schema_bounds_invalid",
                $"L'attribut « {key} » déclare un minimum supérieur à son maximum.");
        }

        return new CategoryAttributeDefinition(
            key,
            string.IsNullOrWhiteSpace(raw.Label) ? key : raw.Label.Trim(),
            type.Value,
            raw.Required ?? false,
            values,
            string.IsNullOrWhiteSpace(raw.Unit) ? null : raw.Unit.Trim(),
            raw.Min,
            raw.Max);
    }

    private static CategoryAttributeType? ParseType(string? type)
        => type?.Trim().ToLowerInvariant() switch
        {
            null or "" or "text" or "string" => CategoryAttributeType.Text,
            "number" or "decimal" or "int" or "integer" => CategoryAttributeType.Number,
            "boolean" or "bool" => CategoryAttributeType.Boolean,
            "enum" or "select" or "list" => CategoryAttributeType.Enum,
            _ => null
        };

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// CONTRÔLE LES ATTRIBUTS D'UN PRODUIT CONTRE CE SCHÉMA.
    ///
    /// TOUS LES DÉFAUTS SONT RASSEMBLÉS EN UN SEUL MESSAGE. Un formulaire qui refuse
    /// une erreur à la fois oblige le vendeur à autant d'allers-retours qu'il a fait
    /// de fautes — et chacun de ces allers-retours, sur une fiche produit, veut dire
    /// retéléverser des images.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result Validate(IReadOnlyDictionary<string, string>? attributes)
    {
        if (IsEmpty)
        {
            return Result.Success();
        }

        var provided = attributes ?? new Dictionary<string, string>();
        var problems = new List<string>();

        foreach (var definition in Attributes)
        {
            // La recherche est insensible à la casse : le vendeur saisit « Couleur »
            // là où le schéma dit « couleur », et refuser cela n'apprendrait rien à
            // personne. La NORMALISATION, elle, est faite par l'appelant.
            var value = FindValue(provided, definition.Key);

            if (string.IsNullOrWhiteSpace(value))
            {
                if (definition.Required)
                {
                    problems.Add($"« {definition.Label} » est obligatoire.");
                }

                continue;
            }

            var problem = ValidateValue(definition, value.Trim());
            if (problem is not null)
            {
                problems.Add(problem);
            }
        }

        if (!AllowUnknownAttributes)
        {
            var known = Attributes.Select(a => a.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in provided.Keys.Where(k => !known.Contains(k)))
            {
                problems.Add($"L'attribut « {key} » n'est pas attendu dans cette catégorie.");
            }
        }

        return problems.Count == 0
            ? Result.Success()
            : Result.Failure(Error.Validation(
                "catalog.product.attributes_invalid",
                string.Join(" ", problems)));
    }

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// RAMÈNE LES CLÉS À CELLES DU SCHÉMA.
    ///
    /// C'est la moitié utile du travail, et celle que la seule validation ne fait pas.
    /// Sans elle, un vendeur écrit « Couleur », le suivant « couleur », un troisième
    /// « COULEUR » : trois clés en base pour une même notion, donc trois facettes de
    /// recherche, et aucun filtre qui fonctionne. Le contrôle accepte les trois — il
    /// serait absurde de refuser une majuscule — mais ce qui est ÉCRIT est la clé
    /// canonique du schéma.
    ///
    /// Les attributs hors schéma sont conservés tels quels quand la catégorie les
    /// autorise : on ne canonise que ce dont on connaît la forme canonique.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null || attributes.Count == 0 || Attributes.Count == 0)
        {
            return attributes ?? new Dictionary<string, string>();
        }

        var canonical = Attributes.ToDictionary(a => a.Key, a => a.Key, StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, string>(attributes.Count, StringComparer.Ordinal);

        foreach (var pair in attributes)
        {
            var key = canonical.TryGetValue(pair.Key.Trim(), out var known) ? known : pair.Key.Trim();

            // Deux graphies d'une même clé dans la MÊME saisie : la première gagne.
            // Écraser silencieusement laisserait le vendeur devant une valeur qu'il
            // n'a pas choisie, sans savoir laquelle des deux a été retenue.
            normalized.TryAdd(key, pair.Value.Trim());
        }

        return normalized;
    }

    private static string? FindValue(IReadOnlyDictionary<string, string> attributes, string key)
    {
        if (attributes.TryGetValue(key, out var direct))
        {
            return direct;
        }

        foreach (var pair in attributes)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static string? ValidateValue(CategoryAttributeDefinition definition, string value)
    {
        switch (definition.Type)
        {
            case CategoryAttributeType.Number:
                // InvariantCulture : la valeur est stockée telle quelle et relue par
                // n'importe quel hôte. La laisser dépendre de la culture ambiante
                // ferait qu'un « 1,5 » accepté ici serait refusé ailleurs.
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    return $"« {definition.Label} » doit être un nombre.";
                }

                if (definition.Min is { } lower && number < lower)
                {
                    return $"« {definition.Label} » ne peut pas être inférieur à {Format(lower)}{Suffix(definition)}.";
                }

                if (definition.Max is { } upper && number > upper)
                {
                    return $"« {definition.Label} » ne peut pas dépasser {Format(upper)}{Suffix(definition)}.";
                }

                return null;

            case CategoryAttributeType.Boolean:
                return bool.TryParse(value, out _)
                    ? null
                    : $"« {definition.Label} » doit valoir true ou false.";

            case CategoryAttributeType.Enum:
                return definition.Values.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
                    ? null
                    : $"« {definition.Label} » doit être l'une des valeurs : {string.Join(", ", definition.Values)}.";

            case CategoryAttributeType.Text:
            default:
                if (definition.Min is { } minLength && value.Length < minLength)
                {
                    return $"« {definition.Label} » doit comporter au moins {Format(minLength)} caractères.";
                }

                var maxLength = definition.Max ?? MaxValueLength;
                return value.Length > maxLength
                    ? $"« {definition.Label} » ne peut pas dépasser {Format(maxLength)} caractères."
                    : null;
        }
    }

    private static string Format(decimal value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Suffix(CategoryAttributeDefinition definition)
        => definition.Unit is null ? string.Empty : $" {definition.Unit}";

    // ── Forme brute du JSON ─────────────────────────────────────────────────────
    //
    // Types privés : le format stocké est un détail de ce value object. L'extérieur
    // manipule `CategoryAttributeDefinition`, jamais ces enveloppes.

    private sealed class SchemaDocument
    {
        [JsonPropertyName("attributes")]
        public List<RawAttribute>? Attributes { get; set; }

        [JsonPropertyName("allowUnknown")]
        public bool? AllowUnknown { get; set; }
    }

    private sealed class RawAttribute
    {
        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("required")]
        public bool? Required { get; set; }

        [JsonPropertyName("values")]
        public List<string>? Values { get; set; }

        [JsonPropertyName("unit")]
        public string? Unit { get; set; }

        [JsonPropertyName("min")]
        public decimal? Min { get; set; }

        [JsonPropertyName("max")]
        public decimal? Max { get; set; }
    }
}
