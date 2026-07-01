namespace Marketplace.Modules.Identity.Domain.Addresses;

public interface IAddressRepository
{
    Task AddAsync(Address address, CancellationToken cancellationToken = default);

    void Remove(Address address);

    Task<Address?> GetByIdAsync(AddressId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Address>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
