using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Notifications.Application.Notifications.Preferences;

/// <summary>Remplace la liste des catégories dont le push est coupé pour l'utilisateur.</summary>
public sealed record UpdateNotificationPreferencesCommand(Guid UserId, IReadOnlyList<string> MutedCategories) : ICommand;
