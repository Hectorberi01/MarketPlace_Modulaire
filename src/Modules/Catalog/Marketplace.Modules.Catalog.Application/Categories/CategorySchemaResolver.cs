using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE POINT DE PASSAGE UNIQUE DE LA RÉSOLUTION HIÉRARCHIQUE.
///
/// Cinq endroits ont besoin du schéma EFFECTIF d'une catégorie : le contrôle des
/// produits, la lecture unitaire d'une catégorie, la liste de l'arbre, l'aperçu de
/// formulaire, et les deux commandes d'écriture qui doivent refuser une clé déjà
/// portée par un parent.
///
/// Les cinq faisant la même chose, ils divergeraient — c'est le défaut qui court dans
/// ce dépôt et qui a déjà coûté : un contrôle posé sur une surface plutôt que sur la
/// notion. Un seul objet, injecté partout.
///
/// IL NE MET RIEN EN CACHE, DÉLIBÉRÉMENT — ET SON PREMIER CONSOMMATEUR EST LA RAISON.
///
/// `ProductAttributeGuard` l'appelle à chaque création et à chaque modification de
/// produit, et il lit le dépôt SANS passer par aucun cache. C'est voulu : le contrôle
/// d'un produit contre son schéma doit voir l'état RÉEL, pas une vue qui peut avoir un
/// TTL de retard. Un schéma périmé, ici, c'est un produit accepté contre une contrainte
/// qui n'existe plus, ou refusé contre une qui n'existe pas encore.
///
/// Les LECTURES d'affichage, elles, sont mises en cache un étage plus haut
/// (`CatalogCacheKeys.AllCategories` et `CatalogCacheKeys.Category(id)`) — et depuis
/// l'héritage, l'entrée d'une feuille dépend de ses ancêtres : c'est pourquoi
/// `UpdateCategoryCommandHandler` évince aussi toute la descendance.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <remarks>
/// PUBLIC, comme <c>ProductAttributeGuard</c> : c'est l'Infrastructure qui l'enregistre,
/// et elle ne voit pas les internes de l'Application.
/// </remarks>
public sealed class CategorySchemaResolver
{
    private readonly ICategoryRepository _categories;

    public CategorySchemaResolver(ICategoryRepository categories) => _categories = categories;

    /// <summary>
    /// Compose le schéma d'une catégorie avec ceux de toute sa branche ascendante.
    /// </summary>
    public async Task<Result<ResolvedCategorySchema>> ResolveAsync(
        Category category, CancellationToken cancellationToken)
    {
        var ancetres = await _categories.ListAncestorsAsync(category.Path, cancellationToken);
        return ResolvedCategorySchema.Compose(ancetres, category);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// UNE CLÉ DÉJÀ PORTÉE PAR UN ANCÊTRE NE PEUT PAS ÊTRE REDÉCLARÉE.
    ///
    /// C'est la règle qui rend l'héritage lisible. Sans elle, « Ordinateurs portables »
    /// pourrait redéclarer « marque » en facultatif alors qu'« Informatique » l'exige,
    /// et la question « cet attribut est-il obligatoire ? » n'aurait plus de réponse —
    /// elle dépendrait de l'étage d'où l'on regarde.
    ///
    /// LE REFUS NOMME L'ANCÊTRE. « La clé marque est déjà définie » enverrait
    /// l'administrateur chercher dans tout l'arbre ; « déjà définie par Informatique »
    /// lui dit où aller. C'est la différence entre un refus utile et un refus subi.
    ///
    /// ON NE CONTRÔLE QUE LES ANCÊTRES, PAS LES DESCENDANTS. Un parent qui déclare une
    /// clé qu'un enfant portait déjà est un cas réel — et le refuser bloquerait une
    /// remontée légitime : on constate qu'un attribut vaut pour toute la branche, on le
    /// remonte d'un étage. La composition sait déjà résoudre la collision en faveur de
    /// l'ancêtre ; c'est à l'administration de nettoyer l'enfant ensuite, et l'aperçu
    /// le lui montre.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="parentPath">Chemin du parent direct, nul pour une racine.</param>
    /// <param name="schema">Le schéma que la catégorie veut déclarer.</param>
    public async Task<Result> EnsureNoAncestorKeyClashAsync(
        string? parentPath,
        CategoryAttributeSchema schema,
        CancellationToken cancellationToken)
    {
        if (schema.Attributes.Count == 0 || string.IsNullOrWhiteSpace(parentPath))
        {
            return Result.Success();
        }

        // Les ancêtres de la CATÉGORIE sont le parent lui-même plus les ancêtres du
        // parent. On demande donc la branche du parent, en l'incluant : le chemin passé
        // à `ListAncestorsAsync` est celui d'un enfant fictif sous le parent.
        var branche = await _categories.ListAncestorsAsync(
            Category.BuildPath(parentPath, "_"), cancellationToken);

        var portees = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ancetre in branche)
        {
            var lecture = ancetre.ParseSchema();
            if (lecture.IsFailure)
            {
                return Result.Failure(Error.Failure(
                    "catalog.category.ancestor_schema_unreadable",
                    $"Le schéma d'attributs de la catégorie parente « {ancetre.Name} » est illisible : "
                    + "corrigez-le avant de déclarer des attributs sous elle."));
            }

            foreach (var definition in lecture.Value.Attributes)
            {
                portees.TryAdd(definition.Key, ancetre.Name);
            }
        }

        // TOUTES LES COLLISIONS EN UN SEUL MESSAGE, comme le fait déjà `Validate` : un
        // formulaire qui refuse une clé à la fois se corrige en autant d'allers-retours.
        var fautives = schema.Attributes
            .Where(a => portees.ContainsKey(a.Key))
            .Select(a => $"« {a.Key} » (déjà définie par « {portees[a.Key]} »)")
            .ToList();

        return fautives.Count == 0
            ? Result.Success()
            : Result.Failure(Error.Validation(
                "catalog.category.attribute_key_inherited",
                fautives.Count == 1
                    ? $"L'attribut {fautives[0]} est hérité : il s'applique déjà à cette catégorie, "
                      + "inutile de le redéclarer."
                    : $"Ces attributs sont hérités et s'appliquent déjà à cette catégorie : "
                      + string.Join(", ", fautives) + "."));
    }
}
