using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Domain.Notifications;

namespace Marketplace.Modules.Notifications.Application.Notifications.Commands;

/// <summary>Marque une notification comme lue (seul son destinataire le peut).</summary>
public sealed record MarkNotificationReadCommand(Guid NotificationId, Guid RecipientUserId) : ICommand;

/// <summary>Marque toutes les notifications de l'utilisateur comme lues.</summary>
public sealed record MarkAllNotificationsReadCommand(Guid RecipientUserId) : ICommand;

internal sealed class MarkNotificationReadCommandHandler : ICommandHandler<MarkNotificationReadCommand>
{
    private readonly INotificationRepository _repository;
    private readonly INotificationsUnitOfWork _unitOfWork;

    public MarkNotificationReadCommandHandler(INotificationRepository repository, INotificationsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MarkNotificationReadCommand command, CancellationToken cancellationToken)
    {
        var notification = await _repository.GetByIdAsync(new NotificationId(command.NotificationId), cancellationToken);
        if (notification is null)
        {
            return Result.Failure(Error.NotFound("notifications.not_found", "Notification introuvable."));
        }

        if (notification.RecipientUserId != command.RecipientUserId)
        {
            return Result.Failure(Error.Forbidden("notifications.not_owner", "Cette notification ne vous appartient pas."));
        }

        notification.MarkRead();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class MarkAllNotificationsReadCommandHandler : ICommandHandler<MarkAllNotificationsReadCommand>
{
    private readonly INotificationRepository _repository;
    private readonly INotificationsUnitOfWork _unitOfWork;

    public MarkAllNotificationsReadCommandHandler(INotificationRepository repository, INotificationsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MarkAllNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        var unread = await _repository.ListUnreadAsync(command.RecipientUserId, cancellationToken);
        foreach (var notification in unread)
        {
            notification.MarkRead();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
