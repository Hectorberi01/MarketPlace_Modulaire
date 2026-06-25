namespace Marketplace.Modules.Search.Domain.Documents;

/// <summary>
/// Document d'index : projection dénormalisée d'un produit, alimentée par les
/// events d'intégration (Catalog, Offers, Reviews). C'est le read model du
/// module Search — aujourd'hui en Postgres, demain dans Elasticsearch, sans
/// changer les consommateurs. La clé est l'identifiant produit.
/// </summary>
public sealed class SearchDocument
{
    private SearchDocument()
    {
    }

    private SearchDocument(Guid productId, Guid sellerId, Guid categoryId, string name, string slug)
    {
        ProductId = productId;
        SellerId = sellerId;
        CategoryId = categoryId;
        Name = name;
        Slug = slug;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public Guid ProductId { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public decimal? MinPrice { get; private set; }
    public string? Currency { get; private set; }
    public double AverageRating { get; private set; }
    public int ReviewCount { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static SearchDocument FromProduct(Guid productId, Guid sellerId, Guid categoryId, string name, string slug)
        => new(productId, sellerId, categoryId, name, slug);

    public void UpdateProduct(Guid sellerId, Guid categoryId, string name, string slug)
    {
        SellerId = sellerId;
        CategoryId = categoryId;
        Name = name;
        Slug = slug;
        Touch();
    }

    /// <summary>Conserve le prix le plus bas parmi les offres du produit.</summary>
    public void ApplyOfferPrice(decimal price, string currency)
    {
        if (MinPrice is null || price < MinPrice)
        {
            MinPrice = price;
            Currency = currency;
        }

        Touch();
    }

    public void ApplyRating(double average, int count)
    {
        AverageRating = average;
        ReviewCount = count;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
