using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Infrastructure.Persistence;

internal sealed class SellerRepository : ISellerRepository
{
    private readonly SellersDbContext _dbContext;

    public SellerRepository(SellersDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(Seller seller, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers.AddAsync(seller, cancellationToken);

    public void Remove(Seller seller)
        => _dbContext.Sellers.Remove(seller);

    public async Task<Seller?> GetByIdAsync(SellerId id, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers
            .Include(s => s.KybDocuments)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<Seller?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers
            .Include(s => s.KybDocuments)
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

    public async Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers.AnyAsync(s => s.UserId == userId, cancellationToken);

    public async Task<bool> ShopNameExistsAsync(string shopName, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers.AnyAsync(s => s.ShopName == shopName, cancellationToken);

    public async Task<IReadOnlyList<Seller>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Sellers
            .Include(s => s.KybDocuments)
            .OrderBy(s => s.ShopName)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Seller> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts, IReadOnlyDictionary<string, int> KybCounts)> ListPagedAsync(
        int page,
        int pageSize,
        string? search,
        SellerStatus? status,
        KybStatus? kybStatus,
        string? sort,
        bool desc,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.Sellers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ILike = insensible à la CASSE, et à elle seule : « SÈMÈ » et « sèmè » se
            // trouvent, « seme » ne trouve pas « Sèmè ». Rendre la recherche insensible
            // aux accents demanderait l'extension `unaccent`, qui n'est pas installée.
            // Même mécanique que la recherche produits et utilisateurs.
            var term = $"%{search.Trim()}%";
            baseQuery = baseQuery.Where(s => EF.Functions.ILike(s.ShopName, term));
        }

        // Décomptes AVANT les filtres de statut : sélectionner « Suspendu » ne doit pas
        // ramener le compte des actifs à zéro dans les onglets.
        var statusCounts = await baseQuery
            .GroupBy(s => s.Status)
            .Select(g => new { Cle = g.Key, Nombre = g.Count() })
            .ToListAsync(cancellationToken);

        var kybCounts = await baseQuery
            .GroupBy(s => s.KybStatus)
            .Select(g => new { Cle = g.Key, Nombre = g.Count() })
            .ToListAsync(cancellationToken);

        var filtered = baseQuery;
        if (status is { } st)
        {
            filtered = filtered.Where(s => s.Status == st);
        }

        if (kybStatus is { } kyb)
        {
            filtered = filtered.Where(s => s.KybStatus == kyb);
        }

        var total = await filtered.CountAsync(cancellationToken);

        var q = filtered.Include(s => s.KybDocuments);
        IOrderedQueryable<Seller> ordered = sort switch
        {
            "shop" => desc ? q.OrderByDescending(s => s.ShopName) : q.OrderBy(s => s.ShopName),
            // `Status` et `KybStatus` sont persistés en CHAÎNE (HasConversion<string>) :
            // le tri est alphabétique sur le nom de la valeur, pas sur son rang dans
            // l'énumération. « Active, Pending, Suspended », donc, et non l'ordre de
            // déclaration — ce qui est le comportement attendu dans une colonne triable.
            "status" => desc ? q.OrderByDescending(s => s.Status) : q.OrderBy(s => s.Status),
            "kyb" => desc ? q.OrderByDescending(s => s.KybStatus) : q.OrderBy(s => s.KybStatus),
            "sales" => desc ? q.OrderByDescending(s => s.SalesCount) : q.OrderBy(s => s.SalesCount),
            // Par défaut le plus récent d'abord : c'est l'ordre dans lequel les dossiers
            // arrivent, donc celui dans lequel on les traite.
            _ => desc ? q.OrderByDescending(s => s.CreatedOnUtc) : q.OrderBy(s => s.CreatedOnUtc),
        };

        // ═════════════════════════════════════════════════════════════════════════
        // UN DÉPARTAGE, SANS QUOI LA PAGINATION PERD ET RÉPÈTE DES LIGNES.
        //
        // PostgreSQL ne garantit RIEN sur l'ordre des lignes qu'un ORDER BY ne
        // départage pas : deux exécutions de la même requête peuvent les rendre
        // dans un ordre différent. Or `page=1` et `page=2` sont deux exécutions.
        //
        // Sur `sales` et surtout sur `status` et `kyb`, les ex æquo sont la règle et
        // non l'exception — quatre valeurs distinctes pour trois cents boutiques.
        // Concrètement : on trie par statut, on lit la page 1, on passe à la page 2,
        // et une boutique déjà vue réapparaît pendant qu'une autre n'est JAMAIS
        // affichée. Un dossier KYB peut ainsi rester invisible indéfiniment sans que
        // rien ne le signale.
        //
        // La clé primaire départage : elle est unique par construction, donc l'ordre
        // devient total et le découpage en pages, stable.
        // ═════════════════════════════════════════════════════════════════════════
        var items = await ordered
            .ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (
            items,
            total,
            statusCounts.ToDictionary(x => x.Cle.ToString(), x => x.Nombre),
            kybCounts.ToDictionary(x => x.Cle.ToString(), x => x.Nombre));
    }
}
