using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence;

internal sealed class CategoryRepository : ICategoryRepository
{
    private readonly CatalogDbContext _dbContext;

    public CategoryRepository(CatalogDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(Category category, CancellationToken cancellationToken = default)
        => await _dbContext.Categories.AddAsync(category, cancellationToken);

    public void Remove(Category category)
        => _dbContext.Categories.Remove(category);

    public async Task<Category?> GetByIdAsync(CategoryId id, CancellationToken cancellationToken = default)
        => await _dbContext.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Category>> ListAllAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Categories
            .AsNoTracking()
            .OrderBy(c => c.Path)
            .ToListAsync(cancellationToken);

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
    {
        var slugResult = Slug.Create(slug);
        if (slugResult.IsFailure)
        {
            return false;
        }

        var slugValue = slugResult.Value;
        return await _dbContext.Categories.AnyAsync(c => c.Slug == slugValue, cancellationToken);
    }
}
