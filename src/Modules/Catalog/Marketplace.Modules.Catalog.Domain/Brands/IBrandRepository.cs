namespace Marketplace.Modules.Catalog.Domain.Brands;

public interface IBrandRepository
{
    Task AddAsync(Brand brand, CancellationToken cancellationToken = default);

    void Remove(Brand brand);

    Task<Brand?> GetByIdAsync(BrandId id, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);
}
