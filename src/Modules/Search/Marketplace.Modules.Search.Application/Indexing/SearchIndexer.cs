using System.Linq;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Reviews.Contracts;
using Marketplace.Modules.Search.Application.Abstractions;
using Marketplace.Modules.Search.Domain.Documents;

namespace Marketplace.Modules.Search.Application.Indexing;

/// <summary>
/// Projette les events d'intégration dans l'index de recherche. Quand un document
/// manque (events arrivés dans le désordre), il le reconstruit en lisant Catalog
/// via ses Contracts. Centralise la logique d'upsert pour tous les consumers.
/// </summary>
public sealed class SearchIndexer
{
    private readonly ISearchDocumentRepository _repository;
    private readonly ICatalogModuleApi _catalogModuleApi;
    private readonly IReviewsModuleApi _reviewsModuleApi;
    private readonly ISearchUnitOfWork _unitOfWork;

    public SearchIndexer(
        ISearchDocumentRepository repository,
        ICatalogModuleApi catalogModuleApi,
        IReviewsModuleApi reviewsModuleApi,
        ISearchUnitOfWork unitOfWork)
    {
        _repository = repository;
        _catalogModuleApi = catalogModuleApi;
        _reviewsModuleApi = reviewsModuleApi;
        _unitOfWork = unitOfWork;
    }

    public async Task UpsertProductAsync(Guid productId, Guid sellerId, Guid categoryId, string name, string slug, CancellationToken ct)
    {
        var doc = await _repository.GetByProductAsync(productId, ct);
        if (doc is null)
        {
            await _repository.AddAsync(SearchDocument.FromProduct(productId, sellerId, categoryId, name, slug), ct);
        }
        else
        {
            doc.UpdateProduct(sellerId, categoryId, name, slug);
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// RÉÉCRITURE COMPLÈTE D'UN DOCUMENT DEPUIS LE CATALOGUE (reconstruction).
    ///
    /// Distinct d'<see cref="UpsertProductAsync"/>, qui répond à un ÉVÉNEMENT et ne
    /// touche donc qu'à ce que l'événement porte. Ici l'appelant a lu le catalogue
    /// lui-même : il connaît aussi la visibilité et l'image, et il les impose.
    ///
    /// Prix et note ne sont pas touchés — ils appartiennent à Offers et Reviews.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task ReindexProductAsync(
        Guid productId, Guid sellerId, Guid categoryId, string name, string slug,
        bool visible, string? imageUrl, CancellationToken ct)
    {
        var doc = await _repository.GetByProductAsync(productId, ct);
        if (doc is null)
        {
            doc = SearchDocument.FromProduct(productId, sellerId, categoryId, name, slug);
            await _repository.AddAsync(doc, ct);
        }
        else
        {
            doc.UpdateProduct(sellerId, categoryId, name, slug);
        }

        doc.SetImage(imageUrl);
        doc.SetVisible(visible);

        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ApplyOfferPriceAsync(Guid productId, decimal price, string currency, CancellationToken ct)
    {
        var doc = await EnsureAsync(productId, ct);
        if (doc is null)
        {
            return;
        }

        doc.ApplyOfferPrice(price, currency);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Force le prix/devise du document (changement de prix d'une offre) : met à jour
    /// même sans baisse de prix — nécessaire pour répercuter un changement de devise.
    /// </summary>
    public async Task SetOfferPriceAsync(Guid productId, decimal price, string currency, CancellationToken ct)
    {
        var doc = await EnsureAsync(productId, ct);
        if (doc is null)
        {
            return;
        }

        doc.SetOfferPrice(price, currency);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Efface le prix quand le produit n'a plus d'offre. `EnsureAsync` NE reconstruit
    /// PAS de document absent ici : si l'index ne connaît pas ce produit, il n'y a pas
    /// de prix fantôme à effacer, et l'y créer pour le laisser sans prix n'apporterait
    /// rien.
    /// </summary>
    public async Task ClearOfferPriceAsync(Guid productId, CancellationToken ct)
    {
        var doc = await _repository.GetByProductAsync(productId, ct);
        if (doc is null)
        {
            return;
        }

        doc.ClearOfferPrice();
        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Affiche (validation admin → Active) ou masque (Draft / Archived) le produit en vitrine.</summary>
    public async Task SetVisibilityAsync(Guid productId, bool visible, CancellationToken ct)
    {
        var doc = await EnsureAsync(productId, ct);
        if (doc is null)
        {
            return;
        }

        // Rafraîchit la vignette au moment de la mise en vitrine : le produit a
        // alors ses images (ajoutées après création).
        var product = await _catalogModuleApi.GetProductAsync(productId, ct);
        if (product is not null)
        {
            var primary = product.Media.FirstOrDefault(m => m.IsPrimary) ?? product.Media.FirstOrDefault();
            doc.SetImage(primary?.Url);
        }

        doc.SetVisible(visible);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Retire le produit de l'index (suppression côté Catalog) — évite les orphelins en vitrine.</summary>
    public async Task RemoveAsync(Guid productId, CancellationToken ct)
    {
        var doc = await _repository.GetByProductAsync(productId, ct);
        if (doc is null)
        {
            return;
        }

        _repository.Remove(doc);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task RefreshRatingAsync(Guid productId, CancellationToken ct)
    {
        var doc = await EnsureAsync(productId, ct);
        if (doc is null)
        {
            return;
        }

        var rating = await _reviewsModuleApi.GetProductRatingAsync(productId, ct);
        doc.ApplyRating(rating.Average, rating.Count);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Renvoie le document, le reconstruisant depuis Catalog s'il manque.</summary>
    private async Task<SearchDocument?> EnsureAsync(Guid productId, CancellationToken ct)
    {
        var doc = await _repository.GetByProductAsync(productId, ct);
        if (doc is not null)
        {
            return doc;
        }

        var product = await _catalogModuleApi.GetProductAsync(productId, ct);
        if (product is null)
        {
            return null;
        }

        doc = SearchDocument.FromProduct(product.Id, product.SellerId, product.CategoryId, product.Name, product.Slug);
        await _repository.AddAsync(doc, ct);
        return doc;
    }
}
