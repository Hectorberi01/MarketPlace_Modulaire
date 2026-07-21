namespace Marketplace.Modules.Catalog.Domain.Products;

/// <summary>
/// Port de persistance du produit, défini dans le Domain et implémenté en
/// Infrastructure (inversion de dépendance). L'Application dépend de cette
/// abstraction, jamais d'EF Core.
/// </summary>
public interface IProductRepository
{
    Task AddAsync(Product product, CancellationToken cancellationToken = default);

    void Remove(Product product);

    Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default);

    /// <summary>Liste les produits d'un vendeur (back-office vendeur).</summary>
    Task<IReadOnlyList<Product>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Liste TRACKÉE des produits d'un vendeur, destinée à une mutation en masse
    /// (fermeture de compte : dépublication ; suppression : archivage). Contrairement
    /// à <see cref="ListBySellerAsync"/> (AsNoTracking), les entités renvoyées sont
    /// suivies par le contexte, donc leurs changements sont persistés au SaveChanges.
    /// </summary>
    Task<IReadOnlyList<Product>> ListBySellerForUpdateAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Liste tous les produits de la plateforme (gouvernance admin).</summary>
    Task<IReadOnlyList<Product>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Page de produits pour la console admin : recherche par nom, filtre par statut,
    /// tri par date de création décroissante. Renvoie le total filtré + la répartition
    /// par statut (calculée avant le filtre statut).
    /// </summary>
    Task<(IReadOnlyList<Product> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, string? search, ProductStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);
}
