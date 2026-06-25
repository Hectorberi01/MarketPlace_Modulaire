namespace Marketplace.Modules.Search.Contracts;

/// <summary>Un résultat de recherche (produit indexé).</summary>
public sealed record SearchResultItem(
    Guid ProductId,
    Guid SellerId,
    Guid CategoryId,
    string Name,
    string Slug,
    decimal? MinPrice,
    string? Currency,
    double AverageRating,
    int ReviewCount);

/// <summary>Page de résultats de recherche.</summary>
public sealed record SearchResultPage(
    IReadOnlyList<SearchResultItem> Items,
    int Total,
    int Page,
    int PageSize);
