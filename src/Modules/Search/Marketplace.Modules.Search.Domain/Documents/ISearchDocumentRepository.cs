namespace Marketplace.Modules.Search.Domain.Documents;

/// <summary>Critères et résultat paginé d'une recherche produit.</summary>
public sealed record SearchCriteria(string? Text, Guid? CategoryId, string? SortBy, int Page, int PageSize);

public sealed record SearchPage(IReadOnlyList<SearchDocument> Items, int Total, int Page, int PageSize);

public interface ISearchDocumentRepository
{
    Task AddAsync(SearchDocument document, CancellationToken cancellationToken = default);

    Task<SearchDocument?> GetByProductAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<SearchPage> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken = default);
}
