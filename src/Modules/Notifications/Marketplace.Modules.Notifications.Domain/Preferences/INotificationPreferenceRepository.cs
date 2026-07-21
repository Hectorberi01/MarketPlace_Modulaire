namespace Marketplace.Modules.Notifications.Domain.Preferences;

/// <summary>Accès aux préférences de notification — port du domaine.</summary>
public interface INotificationPreferenceRepository
{
    Task<NotificationPreference?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(NotificationPreference preference, CancellationToken cancellationToken = default);
}
