using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence;

internal sealed class ProductRepository : IProductRepository
{
    private readonly CatalogDbContext _dbContext;

    public ProductRepository(CatalogDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
        => await _dbContext.Products.AddAsync(product, cancellationToken);

    public void Remove(Product product)
        => _dbContext.Products.Remove(product);

    public async Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default)
        => await _dbContext.Products
            .Include(p => p.Variants)
            .Include(p => p.Media)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
    {
        var slugResult = Slug.Create(slug);
        if (slugResult.IsFailure)
        {
            return false;
        }

        // Comparaison du VO : EF applique le value converter sur la colonne « slug ».
        var slugValue = slugResult.Value;
        return await _dbContext.Products.AnyAsync(p => p.Slug == slugValue, cancellationToken);
    }
}
