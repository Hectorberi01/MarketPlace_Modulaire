using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Queries.ListProductsBySeller;

/// <summary>Liste les produits d'un vendeur (back-office vendeur).</summary>
public sealed record ListProductsBySellerQuery(Guid SellerId) : IQuery<IReadOnlyList<ProductSummary>>;

internal sealed class ListProductsBySellerQueryHandler
    : IQueryHandler<ListProductsBySellerQuery, IReadOnlyList<ProductSummary>>
{
    private readonly IProductRepository _productRepository;

    public ListProductsBySellerQueryHandler(IProductRepository productRepository)
        => _productRepository = productRepository;

    public async Task<Result<IReadOnlyList<ProductSummary>>> Handle(
        ListProductsBySellerQuery query, CancellationToken cancellationToken)
    {
        var products = await _productRepository.ListBySellerAsync(query.SellerId, cancellationToken);
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
            product.Id.Value,
            product.SellerId,
            product.CategoryId,
            product.BrandId,
            product.Name,
            product.Description,
            product.Slug.Value,
            product.Status.ToString(),
            product.Gtin,
            product.Ean,
            product.ProductGroupId,
            product.Attributes,
            product.Tags.ToList(),
            variants,
            media);
    }
}
