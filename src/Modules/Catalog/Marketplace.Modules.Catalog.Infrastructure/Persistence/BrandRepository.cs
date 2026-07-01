using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Catalog.Domain.Brands;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence;

internal sealed class BrandRepository : IBrandRepository
{
    private readonly CatalogDbContext _dbContext;

    public BrandRepository(CatalogDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(Brand brand, CancellationToken cancellationToken = default)
        => await _dbContext.Brands.AddAsync(brand, cancellationToken);

    public void Remove(Brand brand)
        => _dbContext.Brands.Remove(brand);

    public async Task<Brand?> GetByIdAsync(BrandId id, CancellationToken cancellationToken = default)
        => await _dbContext.Brands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
    {
        var slugResult = Slug.Create(slug);
        if (slugResult.IsFailure)
        {
            return false;
        }

        var slugValue = slugResult.Value;
        return await _dbContext.Brands.AnyAsync(b => b.Slug == slugValue, cancellationToken);
    }

    public async Task<IReadOnlyList<Brand>> ListAllAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Brands
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);
}
