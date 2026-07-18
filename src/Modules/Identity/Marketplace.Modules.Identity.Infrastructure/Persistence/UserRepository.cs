using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Infrastructure.Persistence;

internal sealed class UserRepository : IUserRepository
{
    private readonly IdentityDbContext _dbContext;

    public UserRepository(IdentityDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
        => await _dbContext.Users.AddAsync(user, cancellationToken);

    public void Remove(User user)
        => _dbContext.Users.Remove(user);

    public async Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default)
        => await _dbContext.Users
            .Include(u => u.RoleAssignments)
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<IReadOnlyList<User>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default)
        => await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.RoleAssignments)
            .OrderByDescending(u => u.CreatedOnUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<User> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, string? search, UserStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default)
    {
        // Base = recherche appliquée, filtre statut NON appliqué (pour des facettes stables).
        var baseQuery = _dbContext.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            // ILike (Npgsql) = insensible à la casse. Uniquement sur des colonnes string
            // simples : Email/PhoneNumber sont des value objects convertis, non traduisibles.
            baseQuery = baseQuery.Where(u =>
                EF.Functions.ILike(u.FirstName, term) || EF.Functions.ILike(u.LastName, term));
        }

        var statusCounts = await baseQuery
            .GroupBy(u => u.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var filtered = status is { } s ? baseQuery.Where(u => u.Status == s) : baseQuery;

        var total = await filtered.CountAsync(cancellationToken);

        var q = filtered.Include(u => u.RoleAssignments);
        IOrderedQueryable<User> ordered = sort switch
        {
            "name" => desc ? q.OrderByDescending(u => u.LastName).ThenByDescending(u => u.FirstName)
                           : q.OrderBy(u => u.LastName).ThenBy(u => u.FirstName),
            "status" => desc ? q.OrderByDescending(u => u.Status) : q.OrderBy(u => u.Status),
            _ => desc ? q.OrderByDescending(u => u.CreatedOnUtc) : q.OrderBy(u => u.CreatedOnUtc),
        };

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total, statusCounts.ToDictionary(x => x.Status.ToString(), x => x.Count));
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var emailResult = Email.Create(email);
        if (emailResult.IsFailure)
        {
            return null;
        }

        var value = emailResult.Value;
        return await _dbContext.Users
            .Include(u => u.RoleAssignments)
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == value, cancellationToken);
    }

    public async Task<User?> GetByRefreshTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => await _dbContext.Users
            .Include(u => u.RoleAssignments)
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.RefreshTokens.Any(t => t.TokenHash == tokenHash), cancellationToken);

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        var emailResult = Email.Create(email);
        if (emailResult.IsFailure)
        {
            return false;
        }

        var value = emailResult.Value;
        return await _dbContext.Users.AnyAsync(u => u.Email == value, cancellationToken);
    }

    public async Task<bool> PhoneExistsAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var phoneResult = PhoneNumber.Create(phoneNumber);
        if (phoneResult.IsFailure)
        {
            return false;
        }

        var value = phoneResult.Value;
        return await _dbContext.Users.AnyAsync(u => u.PhoneNumber == value, cancellationToken);
    }

    public async Task<IReadOnlyList<(DateTime Day, int Count)>> SignupsByDayAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        // GROUP BY sur la date (jour) : Npgsql traduit `.Date` en CAST/date côté SQL.
        var rows = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.CreatedOnUtc >= fromUtc && u.CreatedOnUtc < toUtc)
            .GroupBy(u => u.CreatedOnUtc.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderBy(x => x.Day)
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.Day, r.Count)).ToList();
    }
}
