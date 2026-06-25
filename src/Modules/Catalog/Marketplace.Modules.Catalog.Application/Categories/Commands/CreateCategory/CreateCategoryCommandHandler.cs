using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.CreateCategory;

internal sealed class CreateCategoryCommandHandler : ICommandHandler<CreateCategoryCommand, Guid>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public CreateCategoryCommandHandler(ICategoryRepository categoryRepository, ICatalogUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        // Le chemin matérialisé se construit à partir du parent : on le charge.
        string? parentPath = null;
        if (command.ParentId is { } parentId && parentId != Guid.Empty)
        {
            var parent = await _categoryRepository.GetByIdAsync(new CategoryId(parentId), cancellationToken);
            if (parent is null)
            {
                return Error.NotFound("catalog.category.parent_not_found", $"Catégorie parente {parentId} introuvable.");
            }

            parentPath = parent.Path;
        }

        var result = Category.Create(command.Name, command.ParentId, parentPath, command.ImageUrl, command.AttributeSchema);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        var category = result.Value;

        if (await _categoryRepository.SlugExistsAsync(category.Slug.Value, cancellationToken))
        {
            return Error.Conflict("catalog.category.slug_taken", $"La catégorie « {category.Slug.Value} » existe déjà.");
        }

        await _categoryRepository.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return category.Id.Value;
    }
}
