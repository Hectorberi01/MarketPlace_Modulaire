using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Identity.Domain.Roles;

namespace Marketplace.Modules.Identity.Infrastructure.Persistence;

internal sealed class RoleRepository : IRoleRepository
{
    private readonly IdentityDbContext _dbContext;

    public RoleRepository(IdentityDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(Role role, CancellationToken cancellationToken = default)
        => await _dbContext.Roles.AddAsync(role, cancellationToken);

    public void Remove(Role role)
        => _dbContext.Roles.Remove(role);

    public async Task<Role?> GetByIdAsync(RoleId id, CancellationToken cancellationToken = default)
        => await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        => await _dbContext.Roles.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);

    public async Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default)
        => await _dbContext.Roles.AnyAsync(r => r.Name == name, cancellationToken);

    public async Task<IReadOnlyList<Role>> GetByIdsAsync(IEnumerable<RoleId> ids, CancellationToken cancellationToken = default)
    {
        var guidSet = ids.Select(i => i.Value).ToHashSet();
        if (guidSet.Count == 0)
        {
            return Array.Empty<Role>();
        }

        // Table de petite taille : on charge et on filtre en mémoire pour éviter
        // les soucis de traduction du Contains sur une clé à value converter.
        var roles = await _dbContext.Roles.ToListAsync(cancellationToken);
        return roles.Where(r => guidSet.Contains(r.Id.Value)).ToList();
    }

    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Roles.OrderBy(r => r.Name).ToListAsync(cancellationToken);
}
