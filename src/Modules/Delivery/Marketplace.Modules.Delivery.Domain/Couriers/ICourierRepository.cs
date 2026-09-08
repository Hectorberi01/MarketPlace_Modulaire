namespace Marketplace.Modules.Delivery.Domain.Couriers;

public interface ICourierRepository
{
    Task AddAsync(Courier courier, CancellationToken cancellationToken = default);

    Task<Courier?> GetByIdAsync(CourierId id, CancellationToken cancellationToken = default);

    /// <summary>Coursier rattaché à un compte utilisateur (un seul par compte).</summary>
    Task<Courier?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Coursier désigné par son identifiant dans le service de dispatch. C'est par là
    /// qu'arrive une course livrée : le service annonce le coursier avec SA clé, pas
    /// avec la nôtre.
    /// </summary>
    Task<Courier?> GetByExternalRefAsync(string externalRef, CancellationToken cancellationToken = default);

    /// <summary>Un compte utilisateur est-il déjà coursier ? (garde-fou à l'enregistrement)</summary>
    Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Courier>> ListAsync(CourierStatus? status = null, CancellationToken cancellationToken = default);
}
