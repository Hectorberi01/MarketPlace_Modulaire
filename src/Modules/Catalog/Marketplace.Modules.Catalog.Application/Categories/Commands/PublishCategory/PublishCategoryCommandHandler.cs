using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.PublishCategory;

/// <summary>Charge la catégorie et applique la transition de publication du domaine.</summary>
internal sealed class PublishCategoryCommandHandler : ICommandHandler<PublishCategoryCommand, int>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public PublishCategoryCommandHandler(ICategoryRepository categoryRepository, ICatalogUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(PublishCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(new CategoryId(command.CategoryId), cancellationToken);
        if (category is null)
        {
            return Result.Failure<int>(
                Error.NotFound("catalog.category.not_found", $"Catégorie {command.CategoryId} introuvable."));
        }

        var result = category.Publish();
        if (result.IsFailure)
        {
            return Result.Failure<int>(result.Error);
        }

        var published = 1;

        if (command.IncludeDescendants)
        {
            var descendants = await _categoryRepository.ListDescendantsAsync(category.Path, cancellationToken);

            foreach (var descendant in descendants)
            {
                // ─────────────────────────────────────────────────────────────────
                // LES DESCENDANTS ARCHIVÉS SONT IGNORÉS, PAS RESSUSCITÉS.
                //
                // `Publish()` refuse une catégorie archivée. Propager cet échec ferait
                // avorter toute l'opération à cause d'une seule branche retirée
                // volontairement du catalogue ; la publier de force annulerait une
                // décision d'archivage que personne n'a demandé de revenir.
                //
                // On saute donc, et le compteur renvoyé laisse l'administrateur
                // constater l'écart avec ce qu'il attendait.
                // ─────────────────────────────────────────────────────────────────
                if (descendant.Publish().IsFailure)
                {
                    continue;
                }

                published++;
            }
        }

        // Une seule transaction : soit la branche entière bascule, soit rien. Publier
        // à moitié laisserait un arbre incohérent, sans moyen de savoir où reprendre.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return published;
    }
}
