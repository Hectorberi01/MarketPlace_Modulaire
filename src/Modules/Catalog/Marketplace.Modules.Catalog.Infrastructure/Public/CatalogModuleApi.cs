using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application;
using Marketplace.Modules.Catalog.Application.Categories;
using Marketplace.Modules.Catalog.Application.Products;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Brands;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;
using Marketplace.Modules.Catalog.Infrastructure.Persistence;

namespace Marketplace.Modules.Catalog.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module. Lecture seule,
/// AsNoTracking, projetée vers les DTOs de Contracts. Les autres modules ne
/// voient que ça du catalogue.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// C'EST LE CHEMIN LE PLUS CHAUD DE L'APPLICATION.
///
/// La fiche produit mobile (/mobile/products/{id}, anonyme) passe par ici à chaque
/// affichage. Toutes les lectures sont en cache-aside, et partagent leurs clés avec
/// les query handlers correspondants : une même fiche n'est lue en base qu'une fois
/// pour les deux chemins.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
internal sealed class CatalogModuleApi : ICatalogModuleApi
{
    private readonly CatalogDbContext _dbContext;
    private readonly ICacheService _cache;

    public CatalogModuleApi(CatalogDbContext dbContext, ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE FILTRE EST POSÉ APRÈS LE CACHE, ET C'EST DÉLIBÉRÉ.
    ///
    /// Les deux lectures partagent la MÊME entrée de cache : un produit n'est lu en
    /// base qu'une fois pour les deux chemins, et le vendeur qui consulte sa propre
    /// fiche réchauffe celle que verra l'acheteur. Filtrer dans la requête aurait
    /// exigé deux clés — donc deux lectures, deux durées de vie, et deux vérités
    /// possibles pour le même produit.
    ///
    /// Le statut est déjà porté par le résumé mis en cache ; le filtre ne coûte donc
    /// rien qu'une comparaison de chaîne.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<ProductSummary?> GetPubliclyVisibleProductAsync(
        Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(productId, cancellationToken);

        return product is not null
               && string.Equals(product.Status, nameof(ProductStatus.Active), StringComparison.OrdinalIgnoreCase)
            ? product
            : null;
    }

    public Task<ProductSummary?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CatalogCacheKeys.Product(productId),
            async ct =>
            {
                var id = new ProductId(productId);

                var product = await _dbContext.Products
                    .AsNoTracking()
                    .Include(p => p.Variants)
                    .Include(p => p.Media)
                    .FirstOrDefaultAsync(p => p.Id == id, ct);

                return product is null ? null : ProductMapping.ToSummary(product);
            },
            CatalogCacheKeys.ProductTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);

    public Task<BrandSummary?> GetBrandAsync(Guid brandId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CatalogCacheKeys.Brand(brandId),
            async ct =>
            {
                var id = new BrandId(brandId);

                var brand = await _dbContext.Brands
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == id, ct);

                return brand is null
                    ? null
                    : new BrandSummary(brand.Id.Value, brand.Name, brand.Slug.Value, brand.Status.ToString(), brand.LogoUrl, brand.Description);
            },
            CatalogCacheKeys.ReferenceDataTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);

    public Task<CategorySummary?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CatalogCacheKeys.Category(categoryId),
            async ct =>
            {
                var id = new CategoryId(categoryId);

                var category = await _dbContext.Categories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == id, ct);

                return category is null ? null : CategoryMapping.ToSummary(category);
            },
            CatalogCacheKeys.ReferenceDataTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);

    public async Task<IReadOnlyList<ProductSummary>> ListFeaturedAsync(int max, CancellationToken cancellationToken = default)
    {
        var limit = max is < 1 or > 50 ? 12 : max;

        // `Tags` est un text[] natif : le filtre se traduit en `'featured' = ANY(tags)`
        // côté PostgreSQL (index GIN possible). Aucun scan applicatif, aucune colonne
        // dédiée — le tag EST le drapeau.
        var products = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Include(p => p.Media)
            .Where(p => p.Status == ProductStatus.Active && p.Tags.Contains("featured"))
            .OrderByDescending(p => p.CreatedOnUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return products.Select(ProductMapping.ToSummary).ToList();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// PARCOURS D'INDEXATION : PAS DE CACHE, ET C'EST VOULU.
    ///
    /// Toutes les autres lectures de cette classe sont en cache-aside parce
    /// qu'elles servent la fiche produit, appelée à chaque affichage. Celle-ci est
    /// appelée par une reconstruction d'index, c'est-à-dire une fois de temps en
    /// temps, sur la TOTALITÉ du catalogue. La mettre en cache remplirait le cache
    /// de pages que personne ne relira, en évinçant les fiches chaudes — et une
    /// reconstruction doit précisément lire la base, pas une copie.
    ///
    /// TOUS STATUTS CONFONDUS : c'est l'appelant qui décide de la visibilité à
    /// partir de `Status`. Un brouillon doit exister dans l'index en tant
    /// qu'INVISIBLE, faute de quoi sa publication ultérieure n'aurait rien à rendre
    /// visible.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<IReadOnlyList<ProductIndexEntry>> ListForIndexingAsync(
        Guid? afterId, int take, CancellationToken cancellationToken = default)
    {
        var limit = take is < 1 or > MaxIndexingPageSize ? DefaultIndexingPageSize : take;

        var query = _dbContext.Products.AsNoTracking();

        if (afterId is { } cursor)
        {
            query = query.Where(p => p.Id > new ProductId(cursor));
        }

        // L'image principale est projetée en SQL plutôt qu'en chargeant la galerie :
        // une sous-requête par ligne coûte moins qu'un Include qui rapatrie toutes
        // les images de tout le catalogue.
        var rows = await query
            .OrderBy(p => p.Id)
            .Take(limit)
            .Select(p => new
            {
                p.Id,
                p.SellerId,
                p.CategoryId,
                p.Name,
                p.Slug,
                p.Status,
                PrimaryImageUrl = p.Media
                    .OrderByDescending(m => m.IsPrimary)
                    .ThenBy(m => m.Position)
                    .Select(m => m.Url)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ProductIndexEntry(
                r.Id.Value, r.SellerId, r.CategoryId, r.Name, r.Slug.Value, r.Status.ToString(), r.PrimaryImageUrl))
            .ToList();
    }

    /// <summary>Taille de page par défaut d'une reconstruction d'index.</summary>
    private const int DefaultIndexingPageSize = 200;

    /// <summary>
    /// Plafond ABSOLU. Une reconstruction n'a aucune raison de demander plus : au-delà,
    /// c'est la mémoire de l'hôte qui paie, pour un balayage qui de toute façon
    /// reprendra à la page suivante.
    /// </summary>
    private const int MaxIndexingPageSize = 500;
}
