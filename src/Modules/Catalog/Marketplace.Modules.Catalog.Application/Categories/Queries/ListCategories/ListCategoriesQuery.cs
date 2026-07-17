using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories.Queries.ListCategories;

/// <summary>Liste toutes les catégories (accueil de l'app, sélecteur de parent, admin).</summary>
public sealed record ListCategoriesQuery : IQuery<IReadOnlyList<CategorySummary>>;

internal sealed class ListCategoriesQueryHandler : IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategorySummary>>
{
    private readonly ICategoryRepository _repository;
    private readonly ICacheService _cache;

    public ListCategoriesQueryHandler(ICategoryRepository repository, ICacheService cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<Result<IReadOnlyList<CategorySummary>>> Handle(ListCategoriesQuery query, CancellationToken cancellationToken)
    {
        // Le cache le plus rentable de tout le système : quelques dizaines de lignes,
        // modifiées quelques fois par an, relues à CHAQUE ouverture de l'application.
        // Le rapport lecture/écriture se compte en millions pour un.
        var summaries = await _cache.GetOrCreateAsync(
            CatalogCacheKeys.AllCategories,
            async ct =>
            {
                var categories = await _repository.ListAllAsync(ct);
                return categories
                    .Select(c => new CategorySummary(
                        c.Id.Value, c.ParentId, c.Name, c.Slug.Value, c.Path, c.Status.ToString(), c.ImageUrl))
                    .ToList();
            },
            CatalogCacheKeys.ReferenceDataTtl,
            cancellationToken: cancellationToken);

        IReadOnlyList<CategorySummary> result = summaries ?? [];
        return Result.Success(result);
    }
}
