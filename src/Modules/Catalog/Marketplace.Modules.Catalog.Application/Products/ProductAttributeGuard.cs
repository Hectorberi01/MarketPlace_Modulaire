using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Categories;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Products;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE POINT DE PASSAGE UNIQUE : UN PRODUIT RESPECTE LE SCHÉMA DE SA CATÉGORIE.
///
/// Trois chemins créent ou modifient les attributs d'un produit — création simple,
/// création avec images, modification. Recopier le contrôle dans les trois serait
/// reproduire exactement le défaut qui court dans tout ce dépôt : un contrôle posé
/// sur une surface plutôt que sur la notion, qui finit par diverger d'une surface à
/// l'autre. Un seul objet, injecté par les trois.
///
/// IL FAIT DEUX CHOSES, ET LA SECONDE COMPTE AUTANT QUE LA PREMIÈRE.
///
///   1. Il REFUSE ce que le schéma interdit : attribut obligatoire manquant, valeur
///      hors liste, texte là où un nombre est attendu.
///   2. Il NORMALISE les clés sur celles du schéma, pour que « Couleur », « couleur »
///      et « COULEUR » ne fassent pas trois facettes.
///
/// IL VÉRIFIE AUSSI QUE LA CATÉGORIE EXISTE.
///
/// `Product.Create` ne contrôlait que `categoryId != Guid.Empty`, et AUCUN handler ne
/// chargeait la catégorie — il n'y a pas de clé étrangère, les deux tables vivant
/// dans des schémas séparés. Un produit pouvait donc naître sur une catégorie
/// inexistante : invisible dans la navigation par rayon, actif malgré tout, et sans
/// aucun signal au vendeur. Puisqu'il faut désormais charger la catégorie pour lire
/// son schéma, ce contrôle-là ne coûte plus rien.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <remarks>
/// PUBLIC, comme <c>SearchIndexer</c> : c'est le projet Infrastructure qui
/// l'enregistre, et il ne voit pas les internes de l'Application.
/// </remarks>
public sealed class ProductAttributeGuard
{
    private readonly ICategoryRepository _categories;
    private readonly CategorySchemaResolver _resolver;

    public ProductAttributeGuard(ICategoryRepository categories, CategorySchemaResolver resolver)
    {
        _categories = categories;
        _resolver = resolver;
    }

    /// <summary>
    /// Vérifie la catégorie et ses attributs. Renvoie les attributs NORMALISÉS, à
    /// écrire tels quels sur le produit.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE SCHÉMA CONTRÔLÉ EST CELUI DE TOUTE LA BRANCHE, PAS D'UN SEUL ÉTAGE.
    ///
    /// Ce contrôle ne lisait que `category.ParseSchema()`, c'est-à-dire les attributs
    /// que la catégorie déclare ELLE-MÊME. Un produit rangé dans « Informatique ›
    /// Ordinateurs › Ordinateurs portables » n'était donc contraint que par les
    /// attributs de la feuille : ceux d'« Informatique » et d'« Ordinateurs »
    /// n'existaient pas pour lui, alors que c'est précisément là qu'on pose les
    /// attributs communs — marque, garantie, pays de fabrication.
    ///
    /// Rien n'échouait : les fiches étaient acceptées, simplement sans les champs
    /// généraux. La perte ne se voyait qu'à la recherche à facettes, des mois plus
    /// tard, quand « filtrer par marque » ne ramenait rien sur la moitié du rayon.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="forCreation">
    /// Vrai à la CRÉATION d'un produit, et c'est ce qui décide du contrôle de feuille.
    ///
    /// Une modification ne doit JAMAIS être bloquée pour cette raison : des produits
    /// existent dans des catégories intermédiaires, et il suffit d'ajouter une
    /// sous-catégorie sous une catégorie qui porte déjà des fiches pour que celles-ci
    /// cessent d'être dans une feuille. Les enfermer — plus modifiables, y compris pour
    /// en corriger le prix — punirait le vendeur d'une décision de l'administration.
    /// </param>
    public async Task<Result<IReadOnlyDictionary<string, string>>> ResolveAsync(
        Guid categoryId,
        IReadOnlyDictionary<string, string>? attributes,
        CancellationToken cancellationToken,
        bool forCreation = false)
    {
        if (categoryId == Guid.Empty)
        {
            return Error.Validation("catalog.product.category_required", "La catégorie est obligatoire.");
        }

        var category = await _categories.GetByIdAsync(new CategoryId(categoryId), cancellationToken);
        if (category is null)
        {
            return Error.NotFound(
                "catalog.product.category_not_found",
                "Cette catégorie n'existe pas. Choisissez-en une dans l'arbre du catalogue.");
        }

        // ARCHIVÉE : REFUS. BROUILLON : ACCEPTÉ.
        //
        // Une catégorie archivée a été retirée de l'arbre visible : y ranger un
        // nouveau produit, c'est le ranger dans un rayon qui n'existe plus, et le
        // vendeur ne l'apprendrait qu'en constatant que personne ne le trouve.
        //
        // Le brouillon, lui, est un rayon en préparation. Le refuser bloquerait le
        // cas normal où l'administration monte une arborescence et laisse les
        // vendeurs remplir avant l'ouverture — et de toute façon un produit doit
        // être validé avant d'être publié.
        if (category.Status == CategoryStatus.Archived)
        {
            return Error.Conflict(
                "catalog.product.category_archived",
                "Cette catégorie a été retirée du catalogue : choisissez-en une autre.");
        }

        // ═════════════════════════════════════════════════════════════════════════
        // ON NE CRÉE UN PRODUIT QUE DANS UNE FEUILLE.
        //
        // Ranger un ordinateur portable dans « Ordinateurs » plutôt que dans
        // « Ordinateurs portables » ne produit aucune erreur : la fiche est acceptée,
        // simplement privée des attributs de la feuille. Elle devient alors
        // introuvable pour qui filtre sur eux — « taille d'écran », « autonomie » —
        // c'est-à-dire pour l'acheteur qui cherche exactement ce produit.
        //
        // Le vendeur, lui, n'a aucun moyen de s'en apercevoir : son formulaire aura
        // simplement montré moins de champs.
        //
        // À LA CRÉATION SEULEMENT — voir le paramètre `forCreation`.
        // ═════════════════════════════════════════════════════════════════════════
        if (forCreation && await _categories.HasChildrenAsync(categoryId, cancellationToken))
        {
            return Error.Validation(
                "catalog.product.category_not_leaf",
                $"« {category.Name} » contient des sous-catégories : choisissez celle qui décrit "
                + "précisément votre produit. C'est elle qui porte les caractéristiques attendues, "
                + "et c'est par elles que les acheteurs filtrent.");
        }

        // Le schéma EFFECTIF : celui de la catégorie composé avec ceux de tous ses
        // ancêtres. Le message d'un étage illisible nomme l'étage — voir la composition.
        var schemaResult = await _resolver.ResolveAsync(category, cancellationToken);
        if (schemaResult.IsFailure)
        {
            return Result.Failure<IReadOnlyDictionary<string, string>>(schemaResult.Error);
        }

        var schema = schemaResult.Value.Effective;

        var validation = schema.Validate(attributes);
        if (validation.IsFailure)
        {
            return Result.Failure<IReadOnlyDictionary<string, string>>(validation.Error);
        }

        return Result.Success(schema.Normalize(attributes));
    }
}
