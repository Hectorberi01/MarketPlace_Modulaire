namespace Marketplace.Modules.Notifications.Domain.Devices;

/// <summary>Accès aux jetons d'appareil (push) — port du domaine.</summary>
public interface IDeviceTokenRepository
{
    Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task AddAsync(DeviceToken device, CancellationToken cancellationToken = default);
    void Remove(DeviceToken device);

    /// <summary>Jetons actifs d'un utilisateur (tous ses appareils).</summary>
    Task<IReadOnlyList<DeviceToken>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Supprime les jetons devenus invalides (retour « unregistered » de FCM).</summary>
    Task RemoveByTokensAsync(IReadOnlyCollection<string> tokens, CancellationToken cancellationToken = default);
}
