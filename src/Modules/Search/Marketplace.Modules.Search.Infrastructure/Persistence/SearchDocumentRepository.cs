using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Search.Domain.Documents;

namespace Marketplace.Modules.Search.Infrastructure.Persistence;

internal sealed class SearchDocumentRepository : ISearchDocumentRepository
{
    private readonly SearchDbContext _dbContext;

    public SearchDocumentRepository(SearchDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(SearchDocument document, CancellationToken cancellationToken = default)
        => await _dbContext.Documents.AddAsync(document, cancellationToken);

    public async Task<SearchDocument?> GetByProductAsync(Guid productId, CancellationToken cancellationToken = default)
        => await _dbContext.Documents.FirstOrDefaultAsync(d => d.ProductId == productId, cancellationToken);

    public async Task<SearchPage> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Documents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(criteria.Text))
        {
            var pattern = $"%{criteria.Text.Trim()}%";
            query = query.Where(d => EF.Functions.ILike(d.Name, pattern) || EF.Functions.ILike(d.Slug, pattern));
        }

        if (criteria.CategoryId is { } categoryId)
        {
            query = query.Where(d => d.CategoryId == categoryId);
        }

        var total = await query.CountAsync(cancellationToken);

        query = criteria.SortBy?.ToLowerInvariant() switch
        {
            "price" => query.OrderBy(d => d.MinPrice),
            "rating" => query.OrderByDescending(d => d.AverageRating).ThenByDescending(d => d.ReviewCount),
            _ => query.OrderByDescending(d => d.UpdatedAtUtc)
        };

        var items = await query
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        return new SearchPage(items, total, criteria.Page, criteria.PageSize);
    }
}
