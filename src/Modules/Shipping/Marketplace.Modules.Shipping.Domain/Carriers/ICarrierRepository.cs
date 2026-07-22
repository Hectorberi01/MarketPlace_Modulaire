namespace Marketplace.Modules.Shipping.Domain.Carriers;

/// <summary>Accès au catalogue des transporteurs.</summary>
public interface ICarrierRepository
{
    Task AddAsync(Carrier carrier, CancellationToken cancellationToken = default);

    Task<Carrier?> GetByIdAsync(CarrierId id, CancellationToken cancellationToken = default);

    /// <summary>Transporteur par son code (unicité applicative avant l'index base).</summary>
    Task<Carrier?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Tous les transporteurs (admin).</summary>
    Task<IReadOnlyList<Carrier>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Uniquement les transporteurs actifs (choix vendeur).</summary>
    Task<IReadOnlyList<Carrier>> ListActiveAsync(CancellationToken cancellationToken = default);
}
