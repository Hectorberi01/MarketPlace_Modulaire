using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Queries.GetProduct;

internal sealed class GetProductQueryHandler : IQueryHandler<GetProductQuery, ProductSummary>
{
    private readonly IProductRepository _productRepository;

    public GetProductQueryHandler(IProductRepository productRepository)
        => _productRepository = productRepository;

    public async Task<Result<ProductSummary>> Handle(GetProductQuery query, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(query.ProductId), cancellationToken);

        if (product is null)
        {
            return Error.NotFound("catalog.product.not_found", $"Produit {query.ProductId} introuvable.");
        }

        var variants = product.Variants
            .Select(v => new ProductVariantSummary(
                v.Id,
                v.Sku.Value,
                v.VariantAttributes,
                v.Barcode,
                v.WeightGrams))
            .ToList();

        var media = product.Media
            .OrderBy(m => m.Position)
            .Select(m => new ProductMediaSummary(
                m.Id,
                m.Url,
                m.Type.ToString(),
                m.IsPrimary,
                m.Position,
                m.AltText))
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
