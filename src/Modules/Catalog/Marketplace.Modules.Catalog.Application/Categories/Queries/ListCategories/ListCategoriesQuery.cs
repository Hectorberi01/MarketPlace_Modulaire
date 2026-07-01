using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories.Queries.ListCategories;

/// <summary>Liste toutes les catégories (sélecteur de parent, gouvernance admin).</summary>
public sealed record ListCategoriesQuery : IQuery<IReadOnlyList<CategorySummary>>;

internal sealed class ListCategoriesQueryHandler : IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategorySummary>>
{
    private readonly ICategoryRepository _repository;

    public ListCategoriesQueryHandler(ICategoryRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<CategorySummary>>> Handle(ListCategoriesQuery query, CancellationToken cancellationToken)
    {
        var categories = await _repository.ListAllAsync(cancellationToken);
        IReadOnlyList<CategorySummary> summaries = categories
            .Select(c => new CategorySummary(
                c.Id.Value, c.ParentId, c.Name, c.Slug.Value, c.Path, c.Status.ToString(), c.ImageUrl))
            .ToList();
        return Result.Success(summaries);
    }
}
