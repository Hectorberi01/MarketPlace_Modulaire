using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application;
using Marketplace.Modules.Catalog.Application.Products;
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
///
/// ─────────────────────────────────────────────────────────────────────────────
/// C'EST LE CHEMIN LE PLUS CHAUD DE L'APPLICATION.
///
/// La fiche produit mobile (/mobile/products/{id}, anonyme) passe par ici à chaque
/// affichage. Toutes les lectures sont en cache-aside, et partagent leurs clés avec
/// les query handlers correspondants : une même fiche n'est lue en base qu'une fois
/// pour les deux chemins.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
internal sealed class CatalogModuleApi : ICatalogModuleApi
{
    private readonly CatalogDbContext _dbContext;
    private readonly ICacheService _cache;

    public CatalogModuleApi(CatalogDbContext dbContext, ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public Task<ProductSummary?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CatalogCacheKeys.Product(productId),
            async ct =>
            {
                var id = new ProductId(productId);

                var product = await _dbContext.Products
                    .AsNoTracking()
                    .Include(p => p.Variants)
                    .Include(p => p.Media)
                    .FirstOrDefaultAsync(p => p.Id == id, ct);

                return product is null ? null : ProductMapping.ToSummary(product);
            },
            CatalogCacheKeys.ProductTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);

    public Task<BrandSummary?> GetBrandAsync(Guid brandId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CatalogCacheKeys.Brand(brandId),
            async ct =>
            {
                var id = new BrandId(brandId);

                var brand = await _dbContext.Brands
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == id, ct);

                return brand is null
                    ? null
                    : new BrandSummary(brand.Id.Value, brand.Name, brand.Slug.Value, brand.Status.ToString(), brand.LogoUrl, brand.Description);
            },
            CatalogCacheKeys.ReferenceDataTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);

    public Task<CategorySummary?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CatalogCacheKeys.Category(categoryId),
            async ct =>
            {
                var id = new CategoryId(categoryId);

                var category = await _dbContext.Categories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == id, ct);

                return category is null
                    ? null
                    : new CategorySummary(category.Id.Value, category.ParentId, category.Name, category.Slug.Value, category.Path, category.Status.ToString(), category.ImageUrl);
            },
            CatalogCacheKeys.ReferenceDataTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);
}
