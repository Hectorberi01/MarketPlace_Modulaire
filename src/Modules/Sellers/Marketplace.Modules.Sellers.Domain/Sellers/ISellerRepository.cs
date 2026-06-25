namespace Marketplace.Modules.Sellers.Domain.Sellers;

public interface ISellerRepository
{
    Task AddAsync(Seller seller, CancellationToken cancellationToken = default);

    void Remove(Seller seller);

    Task<Seller?> GetByIdAsync(SellerId id, CancellationToken cancellationToken = default);

    Task<Seller?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> ShopNameExistsAsync(string shopName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Seller>> ListAsync(CancellationToken cancellationToken = default);
}
