using Marketplace.BuildingBlocks.Domain.Results;
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

    public ProductAttributeGuard(ICategoryRepository categories) => _categories = categories;

    /// <summary>
    /// Vérifie la catégorie et ses attributs. Renvoie les attributs NORMALISÉS, à
    /// écrire tels quels sur le produit.
    /// </summary>
    public async Task<Result<IReadOnlyDictionary<string, string>>> ResolveAsync(
        Guid categoryId,
        IReadOnlyDictionary<string, string>? attributes,
        CancellationToken cancellationToken)
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

        var schemaResult = category.ParseSchema();
        if (schemaResult.IsFailure)
        {
            // Le schéma est validé à sa saisie ; arriver ici veut dire qu'une donnée
            // a été écrite avant ce contrôle, ou modifiée à la main en base. On ne
            // laisse pas passer en silence — un schéma illisible ne contraint plus
            // rien — mais on nomme la vraie cause plutôt que d'accuser le vendeur.
            return Error.Failure(
                "catalog.category.schema_unreadable",
                "Le schéma d'attributs de cette catégorie est illisible. Signalez-le au support : "
                + "la fiche ne peut pas être contrôlée tant qu'il n'est pas corrigé.");
        }

        var schema = schemaResult.Value;

        var validation = schema.Validate(attributes);
        if (validation.IsFailure)
        {
            return Result.Failure<IReadOnlyDictionary<string, string>>(validation.Error);
        }

        return Result.Success(schema.Normalize(attributes));
    }
}
