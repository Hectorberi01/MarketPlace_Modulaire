using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Identity.Domain.PaymentMethods;

namespace Marketplace.Modules.Identity.Infrastructure.Persistence;

internal sealed class PaymentMethodRepository : IPaymentMethodRepository
{
    private readonly IdentityDbContext _dbContext;

    public PaymentMethodRepository(IdentityDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(PaymentMethod paymentMethod, CancellationToken cancellationToken = default)
        => await _dbContext.PaymentMethods.AddAsync(paymentMethod, cancellationToken);

    public void Remove(PaymentMethod paymentMethod)
        => _dbContext.PaymentMethods.Remove(paymentMethod);

    public async Task<PaymentMethod?> GetByIdAsync(PaymentMethodId id, CancellationToken cancellationToken = default)
        => await _dbContext.PaymentMethods.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PaymentMethod>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.PaymentMethods
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.IsDefault)
            .ThenByDescending(p => p.CreatedOnUtc)
            .ToListAsync(cancellationToken);
}
