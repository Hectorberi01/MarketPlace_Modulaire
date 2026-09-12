using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.DeleteCategory;

/// <summary>
/// Charge la catégorie puis la supprime ; renvoie NotFound si absente.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// SUPPRIMER UN ANCÊTRE LEVAIT LES CONTRAINTES DE TOUTE SA BRANCHE, EN SILENCE.
///
/// Il n'y avait aucun contrôle : ni descendants, ni produits. Les descendants gardaient
/// leur chemin, orphelins dans l'arbre — un défaut d'affichage, tant que le chemin ne
/// servait qu'à afficher.
///
/// L'HÉRITAGE EN FAIT AUTRE CHOSE. Les ancêtres se retrouvent PAR LE CHEMIN : l'étage
/// supprimé cesse d'être trouvé, la composition n'échoue pas — elle compose avec moins
/// d'étages — et tous les attributs que cet étage imposait disparaissent d'un coup, sur
/// toute la branche. Aucune erreur, aucune trace, et des fiches qui cessent d'être
/// contraintes sans que personne ne l'ait décidé.
///
/// ON REFUSE DONC TANT QU'IL RESTE UN DESCENDANT. C'est la même règle que la
/// suppression d'un produit qui porte des offres : on ne détruit pas ce qui soutient
/// autre chose. Le message dit combien de sous-catégories bloquent, pour que
/// l'administration sache ce qu'elle a à faire.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class DeleteCategoryCommandHandler : ICommandHandler<DeleteCategoryCommand>
{
    /// <summary>Au-delà, on cite un nombre plutôt qu'une liste illisible.</summary>
    private const int MaxNomsCites = 5;

    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public DeleteCategoryCommandHandler(ICategoryRepository categoryRepository, ICatalogUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(new CategoryId(command.CategoryId), cancellationToken);
        if (category is null)
        {
            return Result.Failure(Error.NotFound("catalog.category.not_found", $"Catégorie {command.CategoryId} introuvable."));
        }

        var descendants = await _categoryRepository.ListDescendantsAsync(category.Path, cancellationToken);
        if (descendants.Count > 0)
        {
            var noms = descendants.Take(MaxNomsCites).Select(d => $"« {d.Name} »");
            var suite = descendants.Count > MaxNomsCites
                ? $" et {descendants.Count - MaxNomsCites} autre(s)"
                : string.Empty;

            return Result.Failure(Error.Conflict(
                "catalog.category.has_descendants",
                $"« {category.Name} » porte {descendants.Count} sous-catégorie(s) — {string.Join(", ", noms)}{suite}. "
                + "La supprimer retirerait aux produits de toute cette branche les caractéristiques "
                + "qu'elle leur impose, sans que rien ne le signale. Supprimez d'abord les "
                + "sous-catégories, ou archivez celle-ci."));
        }

        _categoryRepository.Remove(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
