using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Queries.ListAllProducts;

/// <summary>Liste tous les produits de la plateforme (gouvernance admin).</summary>
public sealed record ListAllProductsQuery : IQuery<IReadOnlyList<ProductSummary>>;

internal sealed class ListAllProductsQueryHandler : IQueryHandler<ListAllProductsQuery, IReadOnlyList<ProductSummary>>
{
    private readonly IProductRepository _productRepository;

    public ListAllProductsQueryHandler(IProductRepository productRepository) => _productRepository = productRepository;

    /// <summary>
    /// ⚠️ VOLONTAIREMENT NON MISE EN CACHE.
    ///
    /// C'est la vue de gouvernance admin : une poignée de personnes, qui viennent
    /// précisément vérifier l'état RÉEL du catalogue — souvent juste après avoir
    /// modifié quelque chose. Leur servir une vue vieille de cinq minutes, c'est leur
    /// faire douter de leur propre action, et transformer un outil de contrôle en
    /// source de confusion.
    ///
    /// Le trafic est négligeable ; le cache ne rapporterait rien et coûterait la
    /// confiance. Toutes les mises en cache ne sont pas des gains.
    /// </summary>
    public async Task<Result<IReadOnlyList<ProductSummary>>> Handle(ListAllProductsQuery query, CancellationToken cancellationToken)
    {
        var products = await _productRepository.ListAllAsync(cancellationToken: cancellationToken);
        IReadOnlyList<ProductSummary> summaries = products.Select(ProductMapping.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
