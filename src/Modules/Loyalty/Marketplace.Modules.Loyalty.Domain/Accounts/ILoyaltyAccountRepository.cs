namespace Marketplace.Modules.Loyalty.Domain.Accounts;

public interface ILoyaltyAccountRepository
{
    Task AddAsync(LoyaltyAccount account, CancellationToken cancellationToken = default);

    Task<LoyaltyAccount?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
