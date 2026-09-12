using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories.Queries.GetCategoryFormPreview;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// L'APERÇU : CE QUE LE VENDEUR VERRA, VU DEPUIS L'ADMINISTRATION.
///
/// Poser un attribut sur « Informatique » a un effet qu'on ne peut pas se représenter
/// depuis l'écran où on le pose : il apparaîtra dans le formulaire de toutes les
/// feuilles de la branche, mêlé à ceux des étages intermédiaires, dans un ordre et
/// avec des libellés qu'on n'a jamais vus ensemble.
///
/// L'administrateur découvrait donc son schéma composé en ouvrant la console vendeur
/// avec un compte de test — quand il y pensait. Le plus souvent, il ne le découvrait
/// pas du tout, et le constat arrivait par un vendeur qui ne comprend pas ce qu'on lui
/// demande.
///
/// CE N'EST PAS UN RENDU HTML, ET C'EST DÉLIBÉRÉ. On rend le MODÈLE du formulaire —
/// les champs, dans l'ordre, avec leur origine et leurs contraintes — et la console
/// d'administration le passe au MÊME composant que la console vendeur. Rendre du HTML
/// ici aurait produit un aperçu qui ressemble au formulaire sans en être un : deux
/// rendus à maintenir, dont un qui ment dès qu'on touche à l'autre.
///
/// L'APERÇU MARCHE SUR UNE CATÉGORIE INTERMÉDIAIRE, où aucun produit ne peut être créé.
/// C'est précisément là qu'il sert : voir ce qu'on impose plus bas AVANT de le poser.
/// Le champ <c>IsLeaf</c> dit si cette catégorie accueillera réellement des fiches.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record GetCategoryFormPreviewQuery(Guid CategoryId) : IQuery<CategoryFormPreview>;

internal sealed class GetCategoryFormPreviewQueryHandler
    : IQueryHandler<GetCategoryFormPreviewQuery, CategoryFormPreview>
{
    private readonly ICategoryRepository _categories;
    private readonly CategorySchemaResolver _resolver;

    public GetCategoryFormPreviewQueryHandler(
        ICategoryRepository categories, CategorySchemaResolver resolver)
    {
        _categories = categories;
        _resolver = resolver;
    }

    public async Task<Result<CategoryFormPreview>> Handle(
        GetCategoryFormPreviewQuery query, CancellationToken cancellationToken)
    {
        var category = await _categories.GetByIdAsync(new CategoryId(query.CategoryId), cancellationToken);
        if (category is null)
        {
            return Error.NotFound("catalog.category.not_found", $"Catégorie {query.CategoryId} introuvable.");
        }

        // PAS DE CACHE ICI, ET C'EST VOULU. L'aperçu est consulté juste après une
        // modification de schéma, par la personne qui vient de la faire : une réponse
        // mise en cache lui montrerait l'état d'avant, c'est-à-dire exactement ce
        // qu'elle cherche à vérifier qu'il a changé.
        var resolution = await _resolver.ResolveAsync(category, cancellationToken);
        if (resolution.IsFailure)
        {
            return Result.Failure<CategoryFormPreview>(resolution.Error);
        }

        var resolu = resolution.Value;
        var aDesEnfants = await _categories.HasChildrenAsync(category.Id.Value, cancellationToken);

        // Les étages de la branche, dans l'ordre où le vendeur les lira.
        //
        // UN ÉTAGE QUI NE DÉCLARE RIEN N'APPARAÎT PAS, et c'est une conséquence du
        // groupement, pas un choix : une catégorie sans attribut ne contribue aucun
        // élément à `All`, donc aucune clé de groupe. Les rendre tout de même
        // demanderait de partir de la liste des ancêtres. On ne le fait pas : l'aperçu
        // montre CE QUE LE VENDEUR VERRA, et il ne verra pas de section vide.
        var branche = resolu.All
            .GroupBy(a => (a.OwnerCategoryId, a.OwnerCategoryName, a.OwnerCategoryPath, a.Inherited))
            .Select(g => new CategoryFormPreviewGroup(
                g.Key.OwnerCategoryId,
                g.Key.OwnerCategoryName,
                g.Key.OwnerCategoryPath,
                g.Key.Inherited,
                g.Select(a => new CategoryAttributeSummary(
                        a.Definition.Key,
                        a.Definition.Label,
                        a.Definition.Type.ToString().ToLowerInvariant(),
                        a.Definition.Required,
                        a.Definition.Values,
                        a.Definition.Unit,
                        a.Definition.Min,
                        a.Definition.Max,
                        InheritedFrom: a.Inherited ? a.OwnerCategoryName : null,
                        OwnerCategoryId: a.OwnerCategoryId))
                    .ToList()))
            .ToList();

        return new CategoryFormPreview(
            category.Id.Value,
            category.Name,
            category.Path,
            !aDesEnfants,
            resolu.AllowUnknownAttributes,
            resolu.All.Count(a => a.Definition.Required),
            branche,
            resolu.Conflicts
                .Select(c => new CategoryAttributeConflictSummary(
                    c.Key, c.IgnoredCategoryId, c.IgnoredCategoryName, c.WinningCategoryName))
                .ToList());
    }
}
