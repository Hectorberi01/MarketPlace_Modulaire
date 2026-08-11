using HBA.Deliveries.Domain.Deliveries;

namespace HBA.Deliveries.Domain.Drivers;

/// <summary>Accès aux livreurs. L'implémentation vit en Infrastructure.</summary>
public interface IDriverRepository
{
    Task<Driver?> GetByIdAsync(DriverId id, CancellationToken cancellationToken = default);

    /// <summary>Retrouve le livreur rattaché à un compte utilisateur.</summary>
    Task<Driver?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Charge plusieurs livreurs d'un coup, à partir des identifiants renvoyés par
    /// le cache de positions. Un appel par livreur transformerait chaque dispatch
    /// en dizaines d'allers-retours en base, sur le chemin le plus sensible à la
    /// latence de toute l'application.
    /// </summary>
    Task<IReadOnlyList<Driver>> ListByIdsAsync(IReadOnlyCollection<DriverId> ids, CancellationToken cancellationToken = default);

    Task AddAsync(Driver driver, CancellationToken cancellationToken = default);
}
