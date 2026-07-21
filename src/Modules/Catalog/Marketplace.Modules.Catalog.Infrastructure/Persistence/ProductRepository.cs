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

    public async Task<IReadOnlyList<Product>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Include(p => p.Media)
            .Where(p => p.SellerId == sellerId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Product>> ListBySellerForUpdateAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Products
            .Where(p => p.SellerId == sellerId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Product>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default)
        => await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Include(p => p.Media)
            .OrderByDescending(p => p.CreatedOnUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Product> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, string? search, ProductStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            baseQuery = baseQuery.Where(p => EF.Functions.ILike(p.Name, term));
        }

        var statusCounts = await baseQuery
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var filtered = status is { } s ? baseQuery.Where(p => p.Status == s) : baseQuery;

        var total = await filtered.CountAsync(cancellationToken);

        var q = filtered.Include(p => p.Variants).Include(p => p.Media);
        IOrderedQueryable<Product> ordered = sort switch
        {
            "name" => desc ? q.OrderByDescending(p => p.Name) : q.OrderBy(p => p.Name),
            "status" => desc ? q.OrderByDescending(p => p.Status) : q.OrderBy(p => p.Status),
            _ => desc ? q.OrderByDescending(p => p.CreatedOnUtc) : q.OrderBy(p => p.CreatedOnUtc),
        };

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total, statusCounts.ToDictionary(x => x.Status.ToString(), x => x.Count));
    }

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
