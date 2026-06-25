namespace Marketplace.Modules.Identity.Domain.Roles;

public interface IRoleRepository
{
    Task AddAsync(Role role, CancellationToken cancellationToken = default);

    void Remove(Role role);

    Task<Role?> GetByIdAsync(RoleId id, CancellationToken cancellationToken = default);

    Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Role>> GetByIdsAsync(IEnumerable<RoleId> ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken = default);
}
