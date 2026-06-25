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

/// <summary>Met à jour le prix « à partir de » d'un produit (Offers).</summary>
public sealed class OfferCreatedSearchHandler : IIntegrationEventHandler<OfferCreatedIntegrationEvent>
{
    private readonly SearchIndexer _indexer;

    public OfferCreatedSearchHandler(SearchIndexer indexer) => _indexer = indexer;

    public Task HandleAsync(OfferCreatedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _indexer.ApplyOfferPriceAsync(e.ProductId, e.BasePriceAmount, e.Currency, cancellationToken);
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
