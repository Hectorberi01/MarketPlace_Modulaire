using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Shipping.Domain.Shipments;

namespace Marketplace.Modules.Shipping.Infrastructure.Persistence;

internal sealed class ShipmentRepository : IShipmentRepository
{
    private readonly ShippingDbContext _dbContext;

    public ShipmentRepository(ShippingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Shipment shipment, CancellationToken cancellationToken = default)
        => await _dbContext.Shipments.AddAsync(shipment, cancellationToken);

    public async Task<Shipment?> GetByIdAsync(ShipmentId id, CancellationToken cancellationToken = default)
        => await _dbContext.Shipments
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Shipment>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Shipments
            .Include(s => s.Items)
            .Where(s => s.OrderId == orderId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Shipment>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Shipments
            .Include(s => s.Items)
            .Where(s => s.SellerId == sellerId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Shipments.AnyAsync(s => s.OrderId == orderId, cancellationToken);
}
