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

    /// <summary>
    /// Décompte des produits par statut, sans en charger un seul.
    ///
    /// Le tableau de bord demandait « une page d'un produit » à la route paginée pour
    /// n'en lire que le total : deux balayages du catalogue, un tri complet, et le
    /// chargement des variantes et médias d'une ligne dont il ne faisait rien. Un
    /// GROUP BY sur une colonne désormais indexée remplace tout cela.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LES ATTRIBUTS DES PRODUITS RANGÉS DANS CES CATÉGORIES. RIEN D'AUTRE.
    ///
    /// Sert à répondre à une seule question, mais elle est décisive : « si je retire ou
    /// si je durcis cet attribut, est-ce que je casse des produits existants ? ». Sans
    /// elle, l'administration peut rendre obligatoire un champ que trois cents fiches
    /// n'ont pas — et ces fiches deviennent alors non modifiables, y compris pour en
    /// corriger le prix.
    ///
    /// ON NE REND QUE LES DICTIONNAIRES D'ATTRIBUTS, pas les produits : le contrôle
    /// n'a besoin de rien d'autre, et charger les agrégats entiers (variantes et médias
    /// compris) pour lire une colonne serait disproportionné.
    ///
    /// LA PORTÉE EST DONNÉE PAR L'APPELANT, sous forme d'identifiants de catégories —
    /// la catégorie modifiée ET toute sa descendance, puisque l'attribut y est hérité.
    /// Le dépôt des catégories sait produire cette liste en une requête sur les chemins.
    ///
    /// Opération d'ADMINISTRATION, rare, sur un périmètre borné à une branche : le coût
    /// est accepté en échange d'une réponse exacte. Un filtre jsonb en base serait plus
    /// économique, mais la colonne passe par un convertisseur de valeur — EF ne sait
    /// pas traduire une recherche à l'intérieur — et le rattraper en SQL brut ferait
    /// dépendre une garantie métier d'une syntaxe PostgreSQL écrite à la main.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ListAttributesByCategoriesAsync(
        IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken = default);
}
