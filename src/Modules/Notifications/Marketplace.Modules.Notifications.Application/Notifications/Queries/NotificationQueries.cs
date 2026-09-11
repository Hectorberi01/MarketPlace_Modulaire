using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Notifications.Contracts;
using Marketplace.Modules.Notifications.Domain.Notifications;

namespace Marketplace.Modules.Notifications.Application.Notifications.Queries;

/// <summary>
/// Liste les notifications de l'utilisateur (les plus récentes d'abord).
///
/// [Skip] EST CE QUI MANQUAIT. Sans lui, la requête ne savait renvoyer QUE la première
/// page : la 51ᵉ notification d'un vendeur n'existait, de son point de vue, nulle part.
/// La valeur par défaut à zéro laisse les appelants existants inchangés.
/// </summary>
public sealed record ListMyNotificationsQuery(Guid RecipientUserId, int Take = 50, int Skip = 0) : IQuery<IReadOnlyList<NotificationSummary>>;

/// <summary>Nombre de notifications non lues de l'utilisateur.</summary>
public sealed record GetUnreadCountQuery(Guid RecipientUserId) : IQuery<int>;

/// <summary>Liste toutes les notifications de la plateforme (back-office admin).</summary>
public sealed record ListAllNotificationsQuery : IQuery<IReadOnlyList<NotificationSummary>>;

internal sealed class ListAllNotificationsQueryHandler : IQueryHandler<ListAllNotificationsQuery, IReadOnlyList<NotificationSummary>>
{
    private readonly INotificationRepository _repository;

    public ListAllNotificationsQueryHandler(INotificationRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<NotificationSummary>>> Handle(ListAllNotificationsQuery query, CancellationToken cancellationToken)
    {
        var notifications = await _repository.ListAllAsync(cancellationToken: cancellationToken);
        IReadOnlyList<NotificationSummary> summaries = notifications.Select(NotificationMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class ListMyNotificationsQueryHandler : IQueryHandler<ListMyNotificationsQuery, IReadOnlyList<NotificationSummary>>
{
    private readonly INotificationRepository _repository;

    public ListMyNotificationsQueryHandler(INotificationRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<NotificationSummary>>> Handle(ListMyNotificationsQuery query, CancellationToken cancellationToken)
    {
        // ON BORNE, ON NE REMET PAS À 50. `take=300` retombait à cinquante, c'est-à-dire
        // à MOINS que le plafond demandé : un client qui pagine y voyait des pages plus
        // petites que prévu et concluait à une fin de liste. Un `take` absurde vaut la
        // borne la plus proche, pas une troisième valeur sortie de nulle part.
        var take = Math.Clamp(query.Take < 1 ? 50 : query.Take, 1, 200);

        // Un décalage négatif deviendrait un `Skip(-n)` — que PostgreSQL refuse à
        // l'exécution, pas à la traduction : l'erreur serait une exception de base de
        // données sur une saisie d'URL. On borne plutôt.
        var skip = query.Skip < 0 ? 0 : query.Skip;

        var notifications = await _repository.ListByRecipientAsync(query.RecipientUserId, take, skip, cancellationToken);
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
