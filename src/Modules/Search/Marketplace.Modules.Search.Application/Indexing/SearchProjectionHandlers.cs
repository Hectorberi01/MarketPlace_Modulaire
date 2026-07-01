using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Catalog.Contracts.IntegrationEvents;
using Marketplace.Modules.Offers.Contracts.IntegrationEvents;
using Marketplace.Modules.Reviews.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Search.Application.Indexing;

/// <summary>Indexe un produit nouvellement créé (Catalog).</summary>
public sealed class ProductCreatedSearchHandler : IIntegrationEventHandler<ProductCreatedIntegrationEvent>
{
    private readonly SearchIndexer _indexer;

    public ProductCreatedSearchHandler(SearchIndexer indexer) => _indexer = indexer;

    public Task HandleAsync(ProductCreatedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _indexer.UpsertProductAsync(e.ProductId, e.SellerId, e.CategoryId, e.Name, e.Slug, cancellationToken);
}

/// <summary>
/// Gère la visibilité publique selon le statut validé par l'admin : « Active »
/// rend le produit visible en vitrine, « Draft » / « Archived » le masque.
/// </summary>
public sealed class ProductStatusChangedSearchHandler : IIntegrationEventHandler<ProductStatusChangedIntegrationEvent>
{
    private readonly SearchIndexer _indexer;

    public ProductStatusChangedSearchHandler(SearchIndexer indexer) => _indexer = indexer;

    public Task HandleAsync(ProductStatusChangedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _indexer.SetVisibilityAsync(
            e.ProductId,
            string.Equals(e.Status, "Active", StringComparison.OrdinalIgnoreCase),
            cancellationToken);
}

/// <summary>Retire le produit de l'index quand il est supprimé du catalogue (Catalog).</summary>
public sealed class ProductDeletedSearchHandler : IIntegrationEventHandler<ProductDeletedIntegrationEvent>
{
    private readonly SearchIndexer _indexer;

    public ProductDeletedSearchHandler(SearchIndexer indexer) => _indexer = indexer;

    public Task HandleAsync(ProductDeletedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _indexer.RemoveAsync(e.ProductId, cancellationToken);
}

/// <summary>Met à jour le prix « à partir de » d'un produit (Offers).</summary>
public sealed class OfferCreatedSearchHandler : IIntegrationEventHandler<OfferCreatedIntegrationEvent>
{
    private readonly SearchIndexer _indexer;

    public OfferCreatedSearchHandler(SearchIndexer indexer) => _indexer = indexer;

    public Task HandleAsync(OfferCreatedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _indexer.ApplyOfferPriceAsync(e.ProductId, e.BasePriceAmount, e.Currency, cancellationToken);
}

/// <summary>Met à jour le prix/devise « à partir de » quand une offre change de prix (Offers).</summary>
public sealed class OfferPriceChangedSearchHandler : IIntegrationEventHandler<OfferPriceChangedIntegrationEvent>
{
    private readonly SearchIndexer _indexer;

    public OfferPriceChangedSearchHandler(SearchIndexer indexer) => _indexer = indexer;

    public Task HandleAsync(OfferPriceChangedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _indexer.SetOfferPriceAsync(e.ProductId, e.BasePriceAmount, e.Currency, cancellationToken);
}

/// <summary>Rafraîchit la note d'un produit après publication d'un avis (Reviews).</summary>
public sealed class ReviewPublishedSearchHandler : IIntegrationEventHandler<ReviewPublishedIntegrationEvent>
{
    private readonly SearchIndexer _indexer;

    public ReviewPublishedSearchHandler(SearchIndexer indexer) => _indexer = indexer;

    public Task HandleAsync(ReviewPublishedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _indexer.RefreshRatingAsync(e.ProductId, cancellationToken);
}

/// <summary>Rafraîchit la note d'un produit après rejet d'un avis (Reviews).</summary>
public sealed class ReviewRejectedSearchHandler : IIntegrationEventHandler<ReviewRejectedIntegrationEvent>
{
    private readonly SearchIndexer _indexer;

    public ReviewRejectedSearchHandler(SearchIndexer indexer) => _indexer = indexer;

    public Task HandleAsync(ReviewRejectedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _indexer.RefreshRatingAsync(e.ProductId, cancellationToken);
}
