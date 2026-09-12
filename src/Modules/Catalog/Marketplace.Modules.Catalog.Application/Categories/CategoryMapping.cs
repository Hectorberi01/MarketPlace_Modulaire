using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories;

/// <summary>
/// Projection <see cref="Category"/> → <see cref="CategorySummary"/>.
///
/// Même raison d'être que <c>ProductMapping</c>, et le même piège à éviter : cette
/// projection était recopiée à l'identique dans trois fichiers — la requête unitaire,
/// la liste, et l'API inter-module — qui PARTAGENT LEUR CLÉ DE CACHE. Deux projections
/// divergentes serviraient donc, sous la même clé, des objets différents selon celui
/// qui a rempli le cache en premier.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// LA PROJECTION A BESOIN DE LA BRANCHE, PLUS SEULEMENT DE LA CATÉGORIE.
///
/// Le schéma servi est désormais le schéma EFFECTIF — celui de la catégorie composé
/// avec ceux de tous ses ancêtres. On ne peut donc plus projeter une catégorie seule :
/// il faut ses ancêtres, et pour la liste complète, aussi ses enfants (pour savoir si
/// c'est une feuille).
///
/// D'où deux surcharges, et une seule règle de composition partagée :
///
///   • `ToSummary(ancêtres, catégorie, aDesEnfants)` — quand on a chargé la branche.
///   • `ProjectAll(toutes)` — pour l'arbre entier : les ancêtres et les enfants se
///     déduisent de la liste en mémoire, SANS une requête de plus. Le cache de l'arbre
///     appelle celle-ci, et c'est le chemin le plus fréquent de très loin.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class CategoryMapping
{
    /// <summary>
    /// Projette une catégorie dont on connaît la branche ascendante.
    /// </summary>
    /// <param name="ancestors">Ancêtres ORDONNÉS de la racine au parent direct.</param>
    /// <param name="hasChildren">Vrai si la catégorie porte au moins un enfant.</param>
    public static CategorySummary ToSummary(
        IReadOnlyList<Category> ancestors,
        Category category,
        bool hasChildren,
        Dictionary<Guid, Result<CategoryAttributeSchema>>? schemaCache = null)
    {
        var composition = ResolvedCategorySchema.Compose(ancestors, category, schemaCache);

        // UN SCHÉMA ILLISIBLE NE FAIT PAS ÉCHOUER UNE LECTURE.
        //
        // `Create` et `Update` refusent un schéma mal formé ; ce cas ne peut donc venir
        // que d'une donnée écrite avant ce contrôle, ou modifiée à la main en base.
        // Faire échouer l'affichage de l'arbre pour autant serait disproportionné : on
        // sert la catégorie sans contrainte affichée. Le refus, lui, tombe au bon
        // endroit — à la création d'un produit dans cette branche, avec un message qui
        // NOMME l'étage fautif.
        if (composition.IsFailure)
        {
            return new CategorySummary(
                category.Id.Value,
                category.ParentId,
                category.Name,
                category.Slug.Value,
                category.Path,
                category.Status.ToString(),
                category.ImageUrl,
                Array.Empty<CategoryAttributeSummary>(),
                // ═══════════════════════════════════════════════════════════════════
                // UN SCHÉMA ILLISIBLE REND « TOUT AUTORISÉ », JAMAIS « RIEN AUTORISÉ ».
                //
                // `schema.IsSuccess && …` disait l'inverse : sur un schéma qu'on ne sait
                // pas lire, il rendait `false`, c'est-à-dire la contrainte la PLUS
                // stricte qui existe — « ce produit ne peut porter aucun attribut ».
                //
                // La console d'administration transformait cette contradiction en perte
                // de données : elle préchargeait « refuser les attributs hors liste »
                // coché, et le premier enregistrement — même un simple renommage —
                // écrivait un schéma valide interdisant tout attribut.
                // ═══════════════════════════════════════════════════════════════════
                AllowUnknownAttributes: true,
                OwnAttributes: Array.Empty<CategoryAttributeSummary>(),
                OwnAllowUnknownAttributes: true,
                InheritedAttributes: Array.Empty<CategoryAttributeSummary>(),
                IsLeaf: !hasChildren,
                SchemaConflicts: Array.Empty<CategoryAttributeConflictSummary>());
        }

        var resolu = composition.Value;

        return new CategorySummary(
            category.Id.Value,
            category.ParentId,
            category.Name,
            category.Slug.Value,
            category.Path,
            category.Status.ToString(),
            category.ImageUrl,
            resolu.All.Select(ToAttribute).ToList(),
            resolu.AllowUnknownAttributes,
            OwnAttributes: resolu.Own.Select(ToAttribute).ToList(),
            // CE QUE LA CATÉGORIE DÉCLARE, pas ce que la branche impose : c'est cette
            // valeur que la console recharge dans son formulaire et réenvoie. Servir
            // la composée ferait écrire la contrainte d'un parent dans le schéma propre
            // de l'enfant, où elle survivrait à sa levée chez le parent.
            OwnAllowUnknownAttributes: resolu.OwnAllowUnknownAttributes,
            InheritedAttributes: resolu.Inherited.Select(ToAttribute).ToList(),
            IsLeaf: !hasChildren,
            SchemaConflicts: resolu.Conflicts
                .Select(c => new CategoryAttributeConflictSummary(
                    c.Key, c.IgnoredCategoryId, c.IgnoredCategoryName, c.WinningCategoryName))
                .ToList());
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// PROJETTE L'ARBRE ENTIER SANS UNE SEULE REQUÊTE SUPPLÉMENTAIRE.
    ///
    /// La composition a besoin, par catégorie, de ses ancêtres et de savoir si elle a
    /// des enfants. Les redemander à la base serait deux requêtes par ligne : sur un
    /// arbre de trois cents catégories, six cents allers-retours pour afficher un
    /// sélecteur. Or la liste complète est DÉJÀ chargée — tout se déduit en mémoire.
    ///
    /// Les ancêtres se retrouvent par le chemin matérialisé, exactement comme en base
    /// (<see cref="Category.AncestorPathsOf"/>) : une même règle, un seul endroit.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <remarks>
    /// LA CHARGE N'EST PAS DANS LES REQUÊTES, ELLE EST DANS LE JSON. Chaque étage d'une
    /// branche est relu par `ParseSchema`, donc DÉSÉRIALISÉ — et chaque catégorie
    /// relit toute sa branche. Sur trois cents catégories de profondeur trois, cela
    /// fait plus de mille désérialisations pour un seul remplissage de cache.
    ///
    /// Le résultat ne dépend que de la catégorie : on le mémorise. Le gain est d'un
    /// facteur égal à la profondeur moyenne de l'arbre, pour un dictionnaire.
    /// </remarks>
    public static IReadOnlyList<CategorySummary> ProjectAll(IReadOnlyList<Category> all)
    {
        var parChemin = new Dictionary<string, Category>(StringComparer.Ordinal);
        foreach (var c in all)
        {
            // Le chemin est unique en base (index). Un doublon ne peut venir que d'une
            // liste construite à la main : la première gagne, comme partout ailleurs.
            parChemin.TryAdd(c.Path, c);
        }

        // MÊME RÈGLE QUE `CategoryRepository.HasChildrenAsync`, FILTRE COMPRIS : une
        // sous-catégorie ARCHIVÉE ne rend pas son parent intermédiaire. Les trois
        // chemins qui calculent « est-ce une feuille ? » — ce dépôt, l'API inter-module
        // et cette projection — alimentent la MÊME clé de cache : un filtre appliqué à
        // deux endroits sur trois ferait dépendre le droit de créer un produit de celui
        // qui a rempli le cache en premier.
        var parents = all
            .Where(c => c.ParentId is not null && c.Status != CategoryStatus.Archived)
            .Select(c => c.ParentId!.Value)
            .ToHashSet();

        // Un seul dictionnaire pour toute la projection — voir la note de `Compose`.
        var schemaCache = new Dictionary<Guid, Result<CategoryAttributeSchema>>(all.Count);
        var resultat = new List<CategorySummary>(all.Count);
        foreach (var category in all)
        {
            var ancetres = new List<Category>();
            foreach (var chemin in Category.AncestorPathsOf(category.Path))
            {
                // ═══════════════════════════════════════════════════════════════
                // UN ANCÊTRE ABSENT NE DEVRAIT PLUS ARRIVER — ET S'IL ARRIVE, ON
                // COMPOSE AVEC MOINS D'ÉTAGES PLUTÔT QUE DE REFUSER D'AFFICHER.
                //
                // C'est la seule entorse au principe de `Compose`, qui refuse toute la
                // branche dès qu'un étage est ILLISIBLE — au motif, juste, qu'un schéma
                // silencieusement amputé est pire qu'un refus. Ici on produit
                // exactement cet amputé. La différence est la conséquence : `Compose`
                // sert le contrôle des produits, où l'amputation laisse passer une
                // fiche non conforme ; ceci sert l'affichage de l'arbre, où refuser
                // rendrait la console inutilisable sans rien réparer.
                //
                // LES DEUX CAUSES CONNUES SONT FERMÉES DEPUIS : le renommage répercute
                // désormais les chemins de toute la descendance
                // (UpdateCategoryCommandHandler), et la suppression d'une catégorie qui
                // porte des descendants est refusée. Reste un import incohérent, ou une
                // ligne écrite à la main.
                // ═══════════════════════════════════════════════════════════════
                if (parChemin.TryGetValue(chemin, out var ancetre))
                {
                    ancetres.Add(ancetre);
                }
            }

            resultat.Add(ToSummary(ancetres, category, parents.Contains(category.Id.Value), schemaCache));
        }

        return resultat;
    }

    private static CategoryAttributeSummary ToAttribute(ResolvedCategoryAttribute a)
        => new(
            a.Definition.Key,
            a.Definition.Label,
            a.Definition.Type.ToString().ToLowerInvariant(),
            a.Definition.Required,
            a.Definition.Values,
            a.Definition.Unit,
            a.Definition.Min,
            a.Definition.Max,
            InheritedFrom: a.Inherited ? a.OwnerCategoryName : null,
            OwnerCategoryId: a.OwnerCategoryId);
}
