namespace Marketplace.Modules.Identity.Domain.Users;

public interface IUserRepository
{
    Task AddAsync(User user, CancellationToken cancellationToken = default);

    void Remove(User user);

    Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<User?> GetByRefreshTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> PhoneExistsAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Liste tous les comptes de la plateforme (back-office admin).</summary>
    Task<IReadOnlyList<User>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default);
}
