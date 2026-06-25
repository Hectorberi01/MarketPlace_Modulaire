using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Notifications.Contracts;
using Marketplace.Modules.Notifications.Domain.Notifications;

namespace Marketplace.Modules.Notifications.Application.Notifications.Queries;

/// <summary>Liste les notifications de l'utilisateur (les plus récentes d'abord).</summary>
public sealed record ListMyNotificationsQuery(Guid RecipientUserId, int Take = 50) : IQuery<IReadOnlyList<NotificationSummary>>;

/// <summary>Nombre de notifications non lues de l'utilisateur.</summary>
public sealed record GetUnreadCountQuery(Guid RecipientUserId) : IQuery<int>;

internal sealed class ListMyNotificationsQueryHandler : IQueryHandler<ListMyNotificationsQuery, IReadOnlyList<NotificationSummary>>
{
    private readonly INotificationRepository _repository;

    public ListMyNotificationsQueryHandler(INotificationRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<NotificationSummary>>> Handle(ListMyNotificationsQuery query, CancellationToken cancellationToken)
    {
        var take = query.Take is < 1 or > 200 ? 50 : query.Take;
        var notifications = await _repository.ListByRecipientAsync(query.RecipientUserId, take, cancellationToken);
        IReadOnlyList<NotificationSummary> summaries = notifications.Select(NotificationMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class GetUnreadCountQueryHandler : IQueryHandler<GetUnreadCountQuery, int>
{
    private readonly INotificationRepository _repository;

    public GetUnreadCountQueryHandler(INotificationRepository repository) => _repository = repository;

    public async Task<Result<int>> Handle(GetUnreadCountQuery query, CancellationToken cancellationToken)
        => await _repository.CountUnreadAsync(query.RecipientUserId, cancellationToken);
}
