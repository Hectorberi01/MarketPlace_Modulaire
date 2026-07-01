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

    /// <summary>Visible en vitrine publique seulement si le produit est validé (Active).</summary>
    public bool IsVisible { get; private set; }

    /// <summary>URL de l'image principale, pour les vignettes (accueil / recherche).</summary>
    public string? ImageUrl { get; private set; }

    public static SearchDocument FromProduct(Guid productId, Guid sellerId, Guid categoryId, string name, string slug)
        => new(productId, sellerId, categoryId, name, slug);

    /// <summary>Affiche (validation admin) ou masque (brouillon / archivé) le produit en vitrine.</summary>
    public void SetVisible(bool visible)
    {
        IsVisible = visible;
        Touch();
    }

    /// <summary>Met à jour la vignette du produit.</summary>
    public void SetImage(string? imageUrl)
    {
        ImageUrl = imageUrl;
        Touch();
    }

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

    /// <summary>
    /// Force le prix/devise affichés (changement de prix d'une offre). Contrairement
    /// à <see cref="ApplyOfferPrice"/>, met à jour même si le prix n'a pas baissé —
    /// indispensable pour répercuter un changement de devise (ou une hausse).
    /// </summary>
    public void SetOfferPrice(decimal price, string currency)
    {
        MinPrice = price;
        Currency = currency;
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
