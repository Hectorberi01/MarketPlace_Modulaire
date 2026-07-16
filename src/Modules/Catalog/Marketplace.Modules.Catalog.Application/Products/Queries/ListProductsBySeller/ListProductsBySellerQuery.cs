using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Queries.ListProductsBySeller;

/// <summary>Liste les produits d'un vendeur (back-office vendeur, page boutique).</summary>
public sealed record ListProductsBySellerQuery(Guid SellerId) : IQuery<IReadOnlyList<ProductSummary>>;

internal sealed class ListProductsBySellerQueryHandler
    : IQueryHandler<ListProductsBySellerQuery, IReadOnlyList<ProductSummary>>
{
    private readonly IProductRepository _productRepository;
    private readonly ICacheService _cache;

    public ListProductsBySellerQueryHandler(IProductRepository productRepository, ICacheService cache)
    {
        _productRepository = productRepository;
        _cache = cache;
    }

    public async Task<Result<IReadOnlyList<ProductSummary>>> Handle(
        ListProductsBySellerQuery query, CancellationToken cancellationToken)
    {
        // Cette requête sert DEUX publics : la page boutique (acheteurs, trafic
        // élevé) et le back-office du vendeur (une personne, qui doit voir son
        // produit dès qu'elle le publie). D'où un TTL court, doublé d'une
        // invalidation à chaque écriture sur un produit de cette boutique — le
        // vendeur relit bien ses propres écritures.
        var summaries = await _cache.GetOrCreateAsync(
            CatalogCacheKeys.ProductsBySeller(query.SellerId),
            async ct =>
            {
                var products = await _productRepository.ListBySellerAsync(query.SellerId, ct);
                return products.Select(ProductMapping.ToSummary).ToList();
            },
            CatalogCacheKeys.SellerProductsTtl,
            cancellationToken: cancellationToken);

        IReadOnlyList<ProductSummary> result = summaries ?? [];
        return Result.Success(result);
    }
}
