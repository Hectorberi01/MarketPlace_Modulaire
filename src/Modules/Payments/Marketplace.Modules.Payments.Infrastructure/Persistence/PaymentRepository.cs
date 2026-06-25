using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Payments.Domain.Payments;

namespace Marketplace.Modules.Payments.Infrastructure.Persistence;

internal sealed class PaymentRepository : IPaymentRepository
{
    private readonly PaymentsDbContext _dbContext;

    public PaymentRepository(PaymentsDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
        => await _dbContext.Payments.AddAsync(payment, cancellationToken);

    public async Task<Payment?> GetByIdAsync(PaymentId id, CancellationToken cancellationToken = default)
        => await _dbContext.Payments.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Payment?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Payments
            .Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Payment?> GetByProviderReferenceAsync(string providerReference, CancellationToken cancellationToken = default)
        => await _dbContext.Payments
            .FirstOrDefaultAsync(p => p.ProviderReference == providerReference, cancellationToken);
}
