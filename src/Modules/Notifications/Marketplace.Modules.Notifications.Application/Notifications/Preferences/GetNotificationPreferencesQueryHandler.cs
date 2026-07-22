using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Notifications.Domain.Preferences;

namespace Marketplace.Modules.Notifications.Application.Notifications.Preferences;

internal sealed class GetNotificationPreferencesQueryHandler
    : IQueryHandler<GetNotificationPreferencesQuery, NotificationPreferencesResult>
{
    private readonly INotificationPreferenceRepository _repository;

    public GetNotificationPreferencesQueryHandler(INotificationPreferenceRepository repository)
        => _repository = repository;

    public async Task<Result<NotificationPreferencesResult>> Handle(
        GetNotificationPreferencesQuery query, CancellationToken cancellationToken)
    {
        var pref = await _repository.GetByUserAsync(query.UserId, cancellationToken);

        var states = NotificationCategories.All
            .Select(key => new NotificationCategoryState(key, pref is null || !pref.IsMuted(key)))
            .ToList();

        return new NotificationPreferencesResult(states);
    }
}
