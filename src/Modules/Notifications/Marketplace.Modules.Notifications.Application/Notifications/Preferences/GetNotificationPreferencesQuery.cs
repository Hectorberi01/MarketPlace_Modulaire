using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Notifications.Application.Notifications.Preferences;

public sealed record GetNotificationPreferencesQuery(Guid UserId) : IQuery<NotificationPreferencesResult>;

/// <summary>État de chaque catégorie pour un utilisateur (Enabled = push actif).</summary>
public sealed record NotificationPreferencesResult(IReadOnlyList<NotificationCategoryState> Categories);

public sealed record NotificationCategoryState(string Key, bool Enabled);
