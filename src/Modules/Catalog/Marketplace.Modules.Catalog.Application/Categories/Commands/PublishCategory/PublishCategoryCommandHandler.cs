using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.PublishCategory;

/// <summary>Charge la catégorie et applique la transition de publication du domaine.</summary>
internal sealed class PublishCategoryCommandHandler : ICommandHandler<PublishCategoryCommand>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public PublishCategoryCommandHandler(ICategoryRepository categoryRepository, ICatalogUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(PublishCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(new CategoryId(command.CategoryId), cancellationToken);
        if (category is null)
        {
            return Result.Failure(Error.NotFound("catalog.category.not_found", $"Catégorie {command.CategoryId} introuvable."));
        }

        var result = category.Publish();
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
