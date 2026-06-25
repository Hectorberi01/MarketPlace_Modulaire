using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.DeleteCategory;

/// <summary>Charge la catégorie puis la supprime ; renvoie NotFound si absente.</summary>
internal sealed class DeleteCategoryCommandHandler : ICommandHandler<DeleteCategoryCommand>
{
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

        _categoryRepository.Remove(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
