namespace Marketplace.Modules.Catalog.Domain.Categories;

public interface ICategoryRepository
{
    Task AddAsync(Category category, CancellationToken cancellationToken = default);

    void Remove(Category category);

    Task<Category?> GetByIdAsync(CategoryId id, CancellationToken cancellationToken = default);

    /// <summary>Liste toutes les catégories (sélecteur parent, gouvernance admin).</summary>
    Task<IReadOnlyList<Category>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);
}
