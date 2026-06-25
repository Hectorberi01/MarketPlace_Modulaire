namespace Marketplace.Modules.Identity.Contracts;

/// <summary>
/// API in-process publique du module Identity. Seul moyen pour un autre module
/// (Sellers, Notifications…) de lire un compte de façon synchrone — jamais en
/// touchant la base ou les entités internes.
/// </summary>
public interface IIdentityModuleApi
{
    Task<UserSummary?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserSummary?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);
}
