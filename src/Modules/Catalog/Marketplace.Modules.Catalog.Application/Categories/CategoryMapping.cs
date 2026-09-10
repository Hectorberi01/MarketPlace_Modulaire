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
/// L'ajout du schéma d'attributs rendait la divergence certaine : trois endroits à
/// modifier, dont un facile à oublier.
/// </summary>
public static class CategoryMapping
{
    public static CategorySummary ToSummary(Category category)
    {
        var schema = category.ParseSchema();

        // UN SCHÉMA ILLISIBLE NE FAIT PAS ÉCHOUER UNE LECTURE.
        //
        // `Create` et `Update` refusent désormais un schéma mal formé ; ce cas ne
        // peut donc venir que d'une donnée écrite avant ce contrôle. Faire échouer
        // l'affichage de l'arbre des catégories pour autant serait disproportionné :
        // on sert la catégorie sans contrainte affichée. Le refus, lui, tombe au bon
        // endroit — à la création d'un produit dans cette catégorie, avec un message
        // qui désigne le schéma.
        // Typage explicite : le `var` d'un ternaire dont une branche est une liste
        // vide n'a pas de type cible, et la collection expression `[]` ne compile pas.
        IReadOnlyList<CategoryAttributeSummary> attributes = schema.IsSuccess
            ? schema.Value.Attributes
                .Select(a => new CategoryAttributeSummary(
                    a.Key,
                    a.Label,
                    a.Type.ToString().ToLowerInvariant(),
                    a.Required,
                    a.Values,
                    a.Unit,
                    a.Min,
                    a.Max))
                .ToList()
            : Array.Empty<CategoryAttributeSummary>();

        return new CategorySummary(
            category.Id.Value,
            category.ParentId,
            category.Name,
            category.Slug.Value,
            category.Path,
            category.Status.ToString(),
            category.ImageUrl,
            attributes,
            // ═══════════════════════════════════════════════════════════════════
            // UN SCHÉMA ILLISIBLE REND « TOUT AUTORISÉ », JAMAIS « RIEN AUTORISÉ ».
            //
            // `schema.IsSuccess && …` disait l'inverse : sur un schéma qu'on ne sait
            // pas lire, il rendait `false`, c'est-à-dire la contrainte la PLUS
            // stricte qui existe — « ce produit ne peut porter aucun attribut ».
            // Deux lignes plus haut, le même échec rend une liste vide et le
            // commentaire annonce « on sert la catégorie sans contrainte affichée » :
            // les deux champs se contredisaient.
            //
            // La console d'administration transformait cette contradiction en perte
            // de données : elle préchargeait « refuser les attributs hors liste »
            // coché, et le premier enregistrement — même un simple renommage —
            // écrivait un schéma valide interdisant tout attribut. Le schéma
            // illisible, lui, était au moins signalé à la création d'un produit.
            // ═══════════════════════════════════════════════════════════════════
            !schema.IsSuccess || schema.Value.AllowUnknownAttributes);
    }
}
