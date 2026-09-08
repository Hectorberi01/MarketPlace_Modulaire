using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Ordering.Domain.Orders;

namespace Marketplace.Modules.Ordering.Infrastructure.Persistence;

internal sealed class OrderRepository : IOrderRepository
{
    private readonly OrderingDbContext _dbContext;

    public OrderRepository(OrderingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
        => await _dbContext.Orders.AddAsync(order, cancellationToken);

    public async Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .Include(o => o.Lines)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<bool> HasPurchasedAsync(Guid buyerId, CancellationToken cancellationToken = default)
        // Pas d'Include, pas de ToList : un EXISTS servi par l'index (BuyerId, Status).
        // Cette lecture est sur le chemin chaud (chaque affichage de panier valorisé) —
        // elle doit rester à quelques microsecondes.
        => await _dbContext.Orders
            .AnyAsync(
                o => o.BuyerId == buyerId
                     && (o.Status == OrderStatus.Paid
                         || o.Status == OrderStatus.Confirmed
                         || o.Status == OrderStatus.Delivered),
                cancellationToken);

    public async Task<IReadOnlyList<Order>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .Where(o => o.Lines.Any(l => l.SellerId == sellerId))
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    // Ni Include ni suivi : on ne remonte que des identifiants. Charger les lignes de
    // cinquante commandes pour n'en garder que la clé serait payer une jointure pour
    // rien, sur un balayage qui tourne en permanence.
    public async Task<IReadOnlyList<Guid>> ListAbandonedIdsAsync(
        DateTime cutoffUtc, int take, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.AwaitingPayment && o.CreatedAtUtc < cutoffUtc)
            .OrderBy(o => o.CreatedAtUtc)
            .Take(take <= 0 ? 50 : take)
            .Select(o => o.Id.Value)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Order>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Order> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, Guid? id, OrderStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.Orders.AsNoTracking().AsQueryable();

        if (id is { } g)
        {
            // Les GUID sont des identifiants exacts (pas de LIKE traduisible dessus) :
            // on rapproche d'une commande OU d'un acheteur.
            var orderId = new OrderId(g);
            baseQuery = baseQuery.Where(o => o.Id == orderId || o.BuyerId == g);
        }

        var statusCounts = await baseQuery
            .GroupBy(o => o.Status)
            .Select(gr => new { Status = gr.Key, Count = gr.Count() })
            .ToListAsync(cancellationToken);

        var filtered = status is { } s ? baseQuery.Where(o => o.Status == s) : baseQuery;

        var total = await filtered.CountAsync(cancellationToken);

        var q = filtered.Include(o => o.Lines);
        IOrderedQueryable<Order> ordered = sort switch
        {
            "total" => desc ? q.OrderByDescending(o => o.GrandTotal) : q.OrderBy(o => o.GrandTotal),
            "status" => desc ? q.OrderByDescending(o => o.Status) : q.OrderBy(o => o.Status),
            _ => desc ? q.OrderByDescending(o => o.CreatedAtUtc) : q.OrderBy(o => o.CreatedAtUtc),
        };

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total, statusCounts.ToDictionary(x => x.Status.ToString(), x => x.Count));
    }
}
