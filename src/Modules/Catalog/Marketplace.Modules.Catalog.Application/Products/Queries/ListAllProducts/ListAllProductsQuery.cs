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

    public async Task<Result<IReadOnlyList<ProductSummary>>> Handle(ListAllProductsQuery query, CancellationToken cancellationToken)
    {
        var products = await _productRepository.ListAllAsync(cancellationToken: cancellationToken);
        IReadOnlyList<ProductSummary> summaries = products.Select(ToSummary).ToList();
        return Result.Success(summaries);
    }

    private static ProductSummary ToSummary(Product product)
    {
        var variants = product.Variants
            .Select(v => new ProductVariantSummary(v.Id, v.Sku.Value, v.VariantAttributes, v.Barcode, v.WeightGrams))
            .ToList();

        var media = product.Media
            .OrderBy(m => m.Position)
            .Select(m => new ProductMediaSummary(m.Id, m.Url, m.Type.ToString(), m.IsPrimary, m.Position, m.AltText))
            .ToList();

        return new ProductSummary(
            product.Id.Value, product.SellerId, product.CategoryId, product.BrandId,
            product.Name, product.Description, product.Slug.Value, product.Status.ToString(), product.Gtin, product.Ean,
            product.ProductGroupId, product.Attributes, product.Tags.ToList(), variants, media);
    }
}
