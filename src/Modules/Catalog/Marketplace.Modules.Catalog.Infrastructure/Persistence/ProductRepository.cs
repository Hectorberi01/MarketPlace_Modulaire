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

    public async Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Products
            .AsNoTracking()
            .GroupBy(p => p.Status)
            .Select(g => new { Statut = g.Key, Nombre = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Statut.ToString(), r => r.Nombre);
    }

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

        // Départage indispensable : PostgreSQL ne garantit aucun ordre entre lignes
        // qu'un ORDER BY laisse ex æquo, et « page 1 » et « page 2 » sont deux
        // exécutions distinctes. Sans lui, un tri par statut — trois ou quatre valeurs
        // pour toute la table — fait réapparaître des lignes d'une page à l'autre et
        // en rend d'autres inatteignables. Voir SellerRepository pour le détail.
        var items = await ordered
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total, statusCounts.ToDictionary(x => x.Status.ToString(), x => x.Count));
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ListAttributesByCategoriesAsync(
        IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken = default)
    {
        if (categoryIds.Count == 0)
        {
            return Array.Empty<IReadOnlyDictionary<string, string>>();
        }

        // PROJECTION SUR LA SEULE COLONNE UTILE. `Attributes` passe par un
        // convertisseur de valeur, qu'EF applique aussi dans une projection : on
        // récupère les dictionnaires sans matérialiser les agrégats, donc sans leurs
        // variantes ni leurs médias.
        var attributs = await _dbContext.Products
            .AsNoTracking()
            .Where(p => categoryIds.Contains(p.CategoryId))
            .Select(p => p.Attributes)
            .ToListAsync(cancellationToken);

        // `Attributes` est déclaré NON annulable et le convertisseur remplace déjà une
        // colonne vide par un dictionnaire vide : il n'y a pas de nul à rattraper ici.
        return attributs
            .Select(a => (IReadOnlyDictionary<string, string>)a)
            .ToList();
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
