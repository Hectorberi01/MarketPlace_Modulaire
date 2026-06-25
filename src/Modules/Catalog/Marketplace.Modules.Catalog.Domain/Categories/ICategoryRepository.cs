namespace Marketplace.Modules.Catalog.Domain.Categories;

public interface ICategoryRepository
{
    Task AddAsync(Category category, CancellationToken cancellationToken = default);

    void Remove(Category category);

    Task<Category?> GetByIdAsync(CategoryId id, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);
}
