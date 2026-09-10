using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Application.Pagination;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.Queries.ListSellers;

/// <summary>
/// Page de vendeurs pour la console d'administration.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// POURQUOI UNE SECONDE REQUÊTE PLUTÔT QUE PAGINER L'EXISTANTE.
///
/// <see cref="ListSellersQuery"/> renvoie TOUS les vendeurs et sert aussi l'API
/// publique (SellerEndpoints). La paginer aurait cassé cet appelant-là pour un besoin
/// qui n'est pas le sien. Deux requêtes, deux usages, aucun compromis.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ListSellersPagedQuery(
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    string? KybStatus = null,
    string? Sort = null,
    string? Dir = null) : IQuery<PagedResult<SellerSummary>>;

internal sealed class ListSellersPagedQueryHandler : IQueryHandler<ListSellersPagedQuery, PagedResult<SellerSummary>>
{
    private readonly ISellerRepository _sellerRepository;

    public ListSellersPagedQueryHandler(ISellerRepository sellerRepository)
        => _sellerRepository = sellerRepository;

    public async Task<Result<PagedResult<SellerSummary>>> Handle(
        ListSellersPagedQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);

        SellerStatus? status = Enum.TryParse<SellerStatus>(query.Status, ignoreCase: true, out var s) ? s : null;
        KybStatus? kyb = Enum.TryParse<KybStatus>(query.KybStatus, ignoreCase: true, out var k) ? k : null;
        bool desc = !string.Equals(query.Dir, "asc", StringComparison.OrdinalIgnoreCase);

        var (sellers, total, statusCounts, kybCounts) =
            await _sellerRepository.ListPagedAsync(page, pageSize, query.Search, status, kyb, query.Sort, desc, cancellationToken);

        // ═════════════════════════════════════════════════════════════════════════
        // DEUX DIMENSIONS DANS UN SEUL DICTIONNAIRE : LES CLÉS SONT PRÉFIXÉES.
        //
        // `PagedResult.Facets` est plat, et le statut de boutique comme l'état du
        // dossier KYB ont chacun une valeur « Pending ». Sans préfixe, l'une écraserait
        // l'autre — et l'écran afficherait un compte de dossiers en attente qui serait
        // en réalité celui des boutiques en attente. Le préfixe est le contrat : le
        // client lit « status:… » et « kyb:… », jamais une clé nue.
        // ═════════════════════════════════════════════════════════════════════════
        var facets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (cle, nombre) in statusCounts)
        {
            facets[$"status:{cle}"] = nombre;
        }

        foreach (var (cle, nombre) in kybCounts)
        {
            facets[$"kyb:{cle}"] = nombre;
        }

        var items = sellers.Select(SellerMapper.ToSummary).ToList();
        return Result.Success(new PagedResult<SellerSummary>(items, total, page, pageSize, facets));
    }
}
