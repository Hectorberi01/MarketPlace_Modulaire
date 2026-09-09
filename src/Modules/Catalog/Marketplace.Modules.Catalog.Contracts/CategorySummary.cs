namespace Marketplace.Modules.Catalog.Contracts;

public sealed record CategorySummary(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string Path,
    string Status,
    string? ImageUrl,
    // ═══════════════════════════════════════════════════════════════════════════════
    // LE SCHÉMA D'ATTRIBUTS DE CETTE CATÉGORIE, SOUS SA FORME LUE.
    //
    // La colonne `attribute_schema` était un jsonb que personne ne lisait. Il pilote
    // désormais la validation des attributs des produits — et pour que le vendeur
    // puisse s'y conformer, il faut d'abord qu'il sache ce qui est attendu. C'est ce
    // que ce champ apporte à l'application vendeur : de quoi dessiner le formulaire
    // (libellés, types, listes de valeurs, champs obligatoires) au lieu d'un champ
    // « attributs » libre où chacun invente ses clés.
    //
    // Liste VIDE = la catégorie n'impose rien, ce qui est le cas de toutes celles qui
    // n'ont jamais renseigné de schéma.
    // ═══════════════════════════════════════════════════════════════════════════════
    IReadOnlyList<CategoryAttributeSummary>? AttributeSchema = null,
    // Faux = le produit peut porter des attributs hors schéma. Vrai par défaut : un
    // schéma décrit ce qu'on attend, pas nécessairement tout ce qui est permis.
    bool AllowUnknownAttributes = true);

/// <summary>
/// Un attribut attendu par une catégorie, tel qu'exposé aux applications. Reflet du
/// value object du domaine, sans en être : les Contracts ne dépendent d'aucune entité.
/// </summary>
/// <param name="Key">Clé technique, à utiliser telle quelle dans les attributs du produit.</param>
/// <param name="Label">Libellé à afficher dans le formulaire.</param>
/// <param name="Type">« text », « number », « boolean » ou « enum ».</param>
/// <param name="Required">Le champ doit-il être renseigné ?</param>
/// <param name="Values">Valeurs admises (type « enum » uniquement).</param>
/// <param name="Unit">Unité affichée à côté du champ, si elle a un sens.</param>
/// <param name="Min">Borne inférieure (nombre) ou longueur minimale (texte).</param>
/// <param name="Max">Borne supérieure (nombre) ou longueur maximale (texte).</param>
public sealed record CategoryAttributeSummary(
    string Key,
    string Label,
    string Type,
    bool Required,
    IReadOnlyList<string> Values,
    string? Unit,
    decimal? Min,
    decimal? Max);
