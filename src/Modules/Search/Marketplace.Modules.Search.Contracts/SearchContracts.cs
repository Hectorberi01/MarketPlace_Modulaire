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
    int ReviewCount,
    string? ImageUrl,
    // Promotion : prix acheteur d'origine (barré) et fin de remise. Laissés nuls par
    // l'index de recherche ; renseignés à la lecture par le BFF mobile depuis l'offre
    // la moins chère du produit. Null = pas de promotion.
    decimal? CompareAtPrice = null,
    DateTime? DiscountEndsOnUtc = null);

/// <summary>Page de résultats de recherche.</summary>
public sealed record SearchResultPage(
    IReadOnlyList<SearchResultItem> Items,
    int Total,
    int Page,
    int PageSize);
