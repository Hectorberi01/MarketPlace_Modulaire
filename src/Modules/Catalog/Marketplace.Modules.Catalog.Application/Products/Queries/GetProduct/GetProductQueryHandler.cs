using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Queries.GetProduct;

internal sealed class GetProductQueryHandler : IQueryHandler<GetProductQuery, ProductSummary>
{
    private readonly IProductRepository _productRepository;
    private readonly ICacheService _cache;

    public GetProductQueryHandler(IProductRepository productRepository, ICacheService cache)
    {
        _productRepository = productRepository;
        _cache = cache;
    }

    public async Task<Result<ProductSummary>> Handle(GetProductQuery query, CancellationToken cancellationToken)
    {
        // Cache-aside. MÊME clé que CatalogModuleApi.GetProductAsync : les deux
        // chemins servent la même fiche, ils partagent donc l'entrée — un seul
        // aller-retour en base réchauffe les deux.
        //
        // L'absence est mémorisée elle aussi : cet endpoint est ANONYME, et sans
        // cache négatif une boucle sur des identifiants au hasard traverserait le
        // cache et frapperait la base à chaque requête.
        var summary = await _cache.GetOrCreateAsync(
            CatalogCacheKeys.Product(query.ProductId),
            async ct =>
            {
                var product = await _productRepository.GetByIdAsync(new ProductId(query.ProductId), ct);
                return product is null ? null : ProductMapping.ToSummary(product);
            },
            CatalogCacheKeys.ProductTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);

        // if/return plutôt qu'un ternaire : les deux branches ont des types
        // différents (Error et ProductSummary) et ne se rejoignent que par les
        // conversions implicites de Result<T>. Ça compile, mais la forme explicite
        // ne laisse aucun doute au lecteur — ni au compilateur.
        if (summary is null)
        {
            return Error.NotFound("catalog.product.not_found", $"Produit {query.ProductId} introuvable.");
        }

        return summary;
    }
}
