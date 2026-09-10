using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Application.Pagination;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Queries.ListAllProducts;

/// <summary>
/// Décompte du catalogue par statut, sans charger de produit.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE TABLEAU DE BORD DEMANDAIT UNE PAGE POUR N'EN LIRE QUE LE TOTAL.
///
/// `GET /admin/products?page=1&pageSize=1` payait, pour afficher un seul entier : un
/// regroupement par statut sur toute la table, un COUNT complet, un tri par date non
/// indexé, et le chargement des variantes et médias de la ligne rendue. Le pire
/// rapport coût/valeur de l'écran.
///
/// La ventilation part avec le total parce qu'elle était DÉJÀ calculée et jetée : la
/// carte « Produits » comptait brouillons et archivés sous une étiquette qui ne le
/// disait pas.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record CountProductsQuery : IQuery<ProductCountSummary>;

/// <summary>Page de produits pour la console admin (recherche par nom, filtre statut).</summary>
public sealed record ListAllProductsQuery(
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    string? Sort = null,
    string? Dir = null) : IQuery<PagedResult<ProductSummary>>;

internal sealed class CountProductsQueryHandler : IQueryHandler<CountProductsQuery, ProductCountSummary>
{
    private readonly IProductRepository _repository;

    public CountProductsQueryHandler(IProductRepository repository) => _repository = repository;

    public async Task<Result<ProductCountSummary>> Handle(CountProductsQuery query, CancellationToken cancellationToken)
    {
        var parStatut = await _repository.CountByStatusAsync(cancellationToken);
        return Result.Success(new ProductCountSummary(parStatut.Values.Sum(), parStatut));
    }
}

internal sealed class ListAllProductsQueryHandler : IQueryHandler<ListAllProductsQuery, PagedResult<ProductSummary>>
{
    private readonly IProductRepository _productRepository;

    public ListAllProductsQueryHandler(IProductRepository productRepository) => _productRepository = productRepository;

    /// <summary>
    /// ⚠️ VOLONTAIREMENT NON MISE EN CACHE.
    ///
    /// C'est la vue de gouvernance admin : une poignée de personnes, qui viennent
    /// précisément vérifier l'état RÉEL du catalogue — souvent juste après avoir
    /// modifié quelque chose. Leur servir une vue vieille de cinq minutes, c'est leur
    /// faire douter de leur propre action, et transformer un outil de contrôle en
    /// source de confusion.
    ///
    /// Le trafic est négligeable ; le cache ne rapporterait rien et coûterait la
    /// confiance. Toutes les mises en cache ne sont pas des gains.
    /// </summary>
    public async Task<Result<PagedResult<ProductSummary>>> Handle(ListAllProductsQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);
        ProductStatus? status = Enum.TryParse<ProductStatus>(query.Status, ignoreCase: true, out var parsed) ? parsed : null;
        bool desc = !string.Equals(query.Dir, "asc", StringComparison.OrdinalIgnoreCase);

        var (products, total, statusCounts) = await _productRepository.ListPagedAsync(page, pageSize, query.Search, status, query.Sort, desc, cancellationToken);
        var items = products.Select(ProductMapping.ToSummary).ToList();
        return Result.Success(new PagedResult<ProductSummary>(items, total, page, pageSize, statusCounts));
    }
}
