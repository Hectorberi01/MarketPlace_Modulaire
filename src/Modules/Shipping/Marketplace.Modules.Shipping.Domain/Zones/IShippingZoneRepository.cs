namespace Marketplace.Modules.Shipping.Domain.Zones;

public interface IShippingZoneRepository
{
    Task AddAsync(ShippingZone zone, CancellationToken cancellationToken = default);

    void Remove(ShippingZone zone);

    Task<ShippingZone?> GetByIdAsync(ShippingZoneId id, CancellationToken cancellationToken = default);

    Task<ShippingZone?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// TOUTES les zones, actives comme inactives.
    ///
    /// Le résolveur filtre lui-même sur `IsActive` : lui donner une liste déjà filtrée
    /// signifierait que la règle de sélection vit à deux endroits — dans le repository
    /// et dans le résolveur — et qu'elle peut diverger. L'écran d'administration a de
    /// toute façon besoin des zones inactives.
    /// </summary>
    Task<IReadOnlyList<ShippingZone>> ListAsync(CancellationToken cancellationToken = default);
}
