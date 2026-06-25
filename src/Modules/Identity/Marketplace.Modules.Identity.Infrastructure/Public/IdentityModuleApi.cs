using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Identity.Domain.Users;
using Marketplace.Modules.Identity.Infrastructure.Persistence;

namespace Marketplace.Modules.Identity.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Identity. Lecture seule,
/// projetée vers le DTO de Contracts. Les autres modules ne voient que ça.
/// </summary>
internal sealed class IdentityModuleApi : IIdentityModuleApi
{
    private readonly IdentityDbContext _dbContext;

    public IdentityModuleApi(IdentityDbContext dbContext)
        => _dbContext = dbContext;

    public async Task<UserSummary?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var id = new UserId(userId);
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.RoleAssignments)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        return user is null ? null : Map(user);
    }

    public async Task<UserSummary?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var emailResult = Email.Create(email);
        if (emailResult.IsFailure)
        {
            return null;
        }

        var value = emailResult.Value;
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.RoleAssignments)
            .FirstOrDefaultAsync(u => u.Email == value, cancellationToken);

        return user is null ? null : Map(user);
    }

    private static UserSummary Map(User user) => new(
        user.Id.Value,
        user.FirstName,
        user.LastName,
        user.Email.Value,
        user.PhoneNumber.Value,
        user.Status.ToString(),
        user.EmailVerified,
        user.MfaEnabled,
        user.RoleIds.ToList());
}
