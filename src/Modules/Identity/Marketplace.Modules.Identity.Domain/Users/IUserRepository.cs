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

    /// <summary>
    /// Page de comptes pour la console admin : recherche (prénom/nom), filtre par
    /// statut, tri par date de création décroissante. Renvoie aussi le total filtré
    /// et la répartition par statut (calculée AVANT le filtre statut, pour un graphe
    /// stable quel que soit le filtre courant).
    /// </summary>
    Task<(IReadOnlyList<User> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, string? search, UserStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nombre d'inscriptions par JOUR sur l'intervalle [fromUtc, toUtc[, pour la
    /// courbe d'évolution des inscriptions de la console. Seuls les jours avec au
    /// moins une inscription sont renvoyés (le front comble les jours vides).
    /// </summary>
    Task<IReadOnlyList<(DateTime Day, int Count)>> SignupsByDayAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
