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

    public async Task<bool> PathExistsAsync(
        string path, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Categories.Where(c => c.Path == path);

        if (excludeId is { } id)
        {
            var categoryId = new CategoryId(id);
            query = query.Where(c => c.Id != categoryId);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Category>> ListDescendantsAsync(
        string path, CancellationToken cancellationToken = default)
    {
        // Le séparateur final délimite la branche : sans lui, « /animaux/chiens »
        // ramasserait « /animaux/chiens-de-chasse », qui n'en est pas un descendant.
        var prefix = path.TrimEnd('/') + "/";

        return await _dbContext.Categories
            .Where(c => c.Path.StartsWith(prefix))
            .OrderBy(c => c.Path)
            .ToListAsync(cancellationToken);
    }
}
