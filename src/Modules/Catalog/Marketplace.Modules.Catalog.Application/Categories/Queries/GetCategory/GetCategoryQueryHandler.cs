using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories.Queries.GetCategory;

internal sealed class GetCategoryQueryHandler : IQueryHandler<GetCategoryQuery, CategorySummary>
{
    private readonly ICategoryRepository _categoryRepository;

    public GetCategoryQueryHandler(ICategoryRepository categoryRepository)
        => _categoryRepository = categoryRepository;

    public async Task<Result<CategorySummary>> Handle(GetCategoryQuery query, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(new CategoryId(query.CategoryId), cancellationToken);

        if (category is null)
        {
            return Error.NotFound("catalog.category.not_found", $"Catégorie {query.CategoryId} introuvable.");
        }

        return new CategorySummary(
            category.Id.Value,
            category.ParentId,
            category.Name,
            category.Slug.Value,
            category.Path,
            category.Status.ToString(),
            category.ImageUrl);
    }
}
