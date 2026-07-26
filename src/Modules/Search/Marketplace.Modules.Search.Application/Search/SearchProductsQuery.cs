using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Search.Contracts;
using Marketplace.Modules.Search.Domain.Documents;

namespace Marketplace.Modules.Search.Application.Search;

/// <summary>Recherche produits dans l'index : texte libre, filtre catégorie, tri, pagination.</summary>
public sealed record SearchProductsQuery(
    string? Text, Guid? CategoryId, string? SortBy, int Page = 1, int PageSize = 20, Guid? SellerId = null,
    // Filtre par ENSEMBLE de catégories (un rayon + tout son sous-arbre). Prioritaire
    // sur CategoryId quand fourni. Résolu côté BFF depuis l'arbre des catégories.
    IReadOnlyList<Guid>? CategoryIds = null) : IQuery<SearchResultPage>;

internal sealed class SearchProductsQueryHandler : IQueryHandler<SearchProductsQuery, SearchResultPage>
{
    private readonly ISearchDocumentRepository _repository;

    public SearchProductsQueryHandler(ISearchDocumentRepository repository) => _repository = repository;

    public async Task<Result<SearchResultPage>> Handle(SearchProductsQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var size = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var result = await _repository.SearchAsync(
            new SearchCriteria(query.Text, query.CategoryId, query.SortBy, page, size, query.SellerId, query.CategoryIds), cancellationToken);

        var items = result.Items.Select(d => new SearchResultItem(
            d.ProductId, d.SellerId, d.CategoryId, d.Name, d.Slug,
            d.MinPrice, d.Currency, d.AverageRating, d.ReviewCount, d.ImageUrl)).ToList();

        return new SearchResultPage(items, result.Total, result.Page, result.PageSize);
    }
}
