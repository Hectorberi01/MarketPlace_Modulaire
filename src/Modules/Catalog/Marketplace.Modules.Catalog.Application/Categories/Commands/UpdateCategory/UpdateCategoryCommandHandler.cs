using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.UpdateCategory;

/// <summary>
/// Recharge le chemin du parent pour recalculer le chemin matérialisé, vérifie
/// l'unicité du slug si le nom change, applique la mise à jour puis persiste.
/// </summary>
internal sealed class UpdateCategoryCommandHandler : ICommandHandler<UpdateCategoryCommand>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public UpdateCategoryCommandHandler(ICategoryRepository categoryRepository, ICatalogUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(new CategoryId(command.CategoryId), cancellationToken);
        if (category is null)
        {
            return Result.Failure(Error.NotFound("catalog.category.not_found", $"Catégorie {command.CategoryId} introuvable."));
        }

        var slugResult = Slug.Create(command.Name);
        if (slugResult.IsFailure)
        {
            return Result.Failure(slugResult.Error);
        }

        var newSlug = slugResult.Value.Value;
        if (newSlug != category.Slug.Value && await _categoryRepository.SlugExistsAsync(newSlug, cancellationToken))
        {
            return Result.Failure(Error.Conflict("catalog.category.slug_taken", $"La catégorie « {newSlug} » existe déjà."));
        }

        string? parentPath = null;
        if (category.ParentId is { } parentId)
        {
            var parent = await _categoryRepository.GetByIdAsync(new CategoryId(parentId), cancellationToken);
            parentPath = parent?.Path;
        }

        var updateResult = category.Update(command.Name, parentPath, command.ImageUrl, command.AttributeSchema);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
