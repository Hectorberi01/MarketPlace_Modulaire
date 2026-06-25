using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Brands;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;
using Marketplace.Modules.Catalog.Infrastructure.Persistence;

namespace Marketplace.Modules.Catalog.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module. Lecture seule,
/// AsNoTracking, projetée vers les DTOs de Contracts. Les autres modules ne
/// voient que ça du catalogue.
/// </summary>
internal sealed class CatalogModuleApi : ICatalogModuleApi
{
    private readonly CatalogDbContext _dbContext;

    public CatalogModuleApi(CatalogDbContext dbContext)
        => _dbContext = dbContext;

    public async Task<ProductSummary?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var id = new ProductId(productId);

        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Include(p => p.Media)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return null;
        }

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
            product.Slug.Value,
            product.Status.ToString(),
            product.Gtin,
            product.Ean,
            product.Tags.ToList(),
            variants,
            media);
    }

    public async Task<BrandSummary?> GetBrandAsync(Guid brandId, CancellationToken cancellationToken = default)
    {
        var id = new BrandId(brandId);

        var brand = await _dbContext.Brands
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        return brand is null
            ? null
            : new BrandSummary(brand.Id.Value, brand.Name, brand.Slug.Value, brand.Status.ToString(), brand.LogoUrl, brand.Description);
    }

    public async Task<CategorySummary?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var id = new CategoryId(categoryId);

        var category = await _dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        return category is null
            ? null
            : new CategorySummary(category.Id.Value, category.ParentId, category.Name, category.Slug.Value, category.Path, category.Status.ToString(), category.ImageUrl);
    }
}
