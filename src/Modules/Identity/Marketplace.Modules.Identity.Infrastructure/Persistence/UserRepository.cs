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
}
