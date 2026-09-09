using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Categories;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories.Queries.GetCategory;

internal sealed class GetCategoryQueryHandler : IQueryHandler<GetCategoryQuery, CategorySummary>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICacheService _cache;

    public GetCategoryQueryHandler(ICategoryRepository categoryRepository, ICacheService cache)
    {
        _categoryRepository = categoryRepository;
        _cache = cache;
    }

    public async Task<Result<CategorySummary>> Handle(GetCategoryQuery query, CancellationToken cancellationToken)
    {
        // Même clé que CatalogModuleApi.GetCategoryAsync : une seule entrée pour les
        // deux chemins de lecture.
        var summary = await _cache.GetOrCreateAsync(
            CatalogCacheKeys.Category(query.CategoryId),
            async ct =>
            {
                var category = await _categoryRepository.GetByIdAsync(new CategoryId(query.CategoryId), ct);
                return category is null ? null : CategoryMapping.ToSummary(category);
            },
            CatalogCacheKeys.ReferenceDataTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);

        if (summary is null)
        {
            return Error.NotFound("catalog.category.not_found", $"Catégorie {query.CategoryId} introuvable.");
        }

        return summary;
    }
}
