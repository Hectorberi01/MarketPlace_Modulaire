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

    public void Remove(SearchDocument document)
        => _dbContext.Documents.Remove(document);

    public async Task<SearchPage> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        // Vitrine publique : uniquement les produits validés (Active).
        var query = _dbContext.Documents.AsNoTracking().Where(d => d.IsVisible);

        if (!string.IsNullOrWhiteSpace(criteria.Text))
        {
            var pattern = $"%{criteria.Text.Trim()}%";
            query = query.Where(d => EF.Functions.ILike(d.Name, pattern) || EF.Functions.ILike(d.Slug, pattern));
        }

        // Filtre catégorie : un ENSEMBLE (rayon + sous-arbre) est prioritaire sur
        // l'identifiant unique. `Contains` se traduit en `category_id IN (…)`.
        if (criteria.CategoryIds is { Count: > 0 } categoryIds)
        {
            query = query.Where(d => categoryIds.Contains(d.CategoryId));
        }
        else if (criteria.CategoryId is { } categoryId)
        {
            query = query.Where(d => d.CategoryId == categoryId);
        }

        if (criteria.SellerId is { } sellerId)
        {
            query = query.Where(d => d.SellerId == sellerId);
        }

        var total = await query.CountAsync(cancellationToken);

        IOrderedQueryable<SearchDocument> ordered = criteria.SortBy?.ToLowerInvariant() switch
        {
            "price" => query.OrderBy(d => d.MinPrice),
            "rating" => query.OrderByDescending(d => d.AverageRating).ThenByDescending(d => d.ReviewCount),
            _ => query.OrderByDescending(d => d.UpdatedAtUtc)
        };

        // ═════════════════════════════════════════════════════════════════════════
        // LA VITRINE PERDAIT DES PRODUITS ENTRE DEUX PAGES.
        //
        // PostgreSQL ne garantit aucun ordre entre lignes qu'un ORDER BY laisse ex
        // æquo, et deux pages sont deux exécutions. Les trois tris d'ici en produisent
        // massivement :
        //
        //   - « note » : tout produit sans avis vaut (0, 0). C'est le plus gros paquet
        //     du catalogue, et c'est celui des NOUVEAUTÉS — un produit fraîchement mis
        //     en ligne pouvait n'apparaître sur AUCUNE page tant qu'il n'avait pas
        //     d'avis, sans que rien ne le signale à son vendeur.
        //   - « prix » : les prix ronds se répètent, et tous les produits sans offre
        //     partagent un MinPrice nul.
        //   - par défaut : les dates se tassent après une reconstruction de l'index,
        //     qui réécrit tout le catalogue en rafale.
        //
        // `ProductId` est la clé primaire de ce document : l'ordre devient total.
        // ═════════════════════════════════════════════════════════════════════════
        var items = await ordered
            .ThenBy(d => d.ProductId)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        return new SearchPage(items, total, criteria.Page, criteria.PageSize);
    }
}
