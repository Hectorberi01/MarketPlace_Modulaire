using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Domain.Notifications;

namespace Marketplace.Modules.Notifications.Application.Notifications.Commands;

/// <summary>Marque une notification comme lue (seul son destinataire le peut).</summary>
public sealed record MarkNotificationReadCommand(Guid NotificationId, Guid RecipientUserId) : ICommand;

/// <summary>Marque toutes les notifications de l'utilisateur comme lues.</summary>
public sealed record MarkAllNotificationsReadCommand(Guid RecipientUserId) : ICommand;

/// <summary>Envoie une notification à un utilisateur (back-office admin).</summary>
public sealed record SendNotificationCommand(Guid RecipientUserId, string? Channel, string Subject, string Body) : ICommand<Guid>;

/// <summary>Supprime une notification (back-office admin, sans contrôle de propriété).</summary>
public sealed record DeleteNotificationCommand(Guid NotificationId) : ICommand;

/// <summary>Supprime SA propre notification (balayage côté app). Vérifie le destinataire.</summary>
public sealed record DeleteOwnNotificationCommand(Guid NotificationId, Guid RecipientUserId) : ICommand;

internal sealed class SendNotificationCommandHandler : ICommandHandler<SendNotificationCommand, Guid>
{
    private readonly INotificationRepository _repository;
    private readonly INotificationsUnitOfWork _unitOfWork;
    private readonly NotificationDispatcher _dispatcher;

    public SendNotificationCommandHandler(
        INotificationRepository repository,
        INotificationsUnitOfWork unitOfWork,
        NotificationDispatcher dispatcher)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _dispatcher = dispatcher;
    }

    public async Task<Result<Guid>> Handle(SendNotificationCommand command, CancellationToken cancellationToken)
    {
        var channel = Enum.TryParse<NotificationChannel>(command.Channel, ignoreCase: true, out var c)
            ? c
            : NotificationChannel.InApp;

        var result = Notification.Create(command.RecipientUserId, channel, command.Subject, command.Body, "Admin", null);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        var notification = result.Value;
        notification.MarkSent();
        await _repository.AddAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Notif in-app créée ; on envoie aussi le push (best-effort) vers les
        // appareils du destinataire — c'est ce qui rend l'envoi admin capable de push.
        await _dispatcher.SendPushAsync(command.RecipientUserId, command.Subject, command.Body, "Admin", null, cancellationToken);

        return notification.Id.Value;
    }
}

internal sealed class DeleteNotificationCommandHandler : ICommandHandler<DeleteNotificationCommand>
{
    private readonly INotificationRepository _repository;
    private readonly INotificationsUnitOfWork _unitOfWork;

    public DeleteNotificationCommandHandler(INotificationRepository repository, INotificationsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteNotificationCommand command, CancellationToken cancellationToken)
    {
        var notification = await _repository.GetByIdAsync(new NotificationId(command.NotificationId), cancellationToken);
        if (notification is null)
        {
            return Result.Failure(Error.NotFound("notifications.not_found", "Notification introuvable."));
        }

        _repository.Remove(notification);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class DeleteOwnNotificationCommandHandler : ICommandHandler<DeleteOwnNotificationCommand>
{
    private readonly INotificationRepository _repository;
    private readonly INotificationsUnitOfWork _unitOfWork;

    public DeleteOwnNotificationCommandHandler(INotificationRepository repository, INotificationsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteOwnNotificationCommand command, CancellationToken cancellationToken)
    {
        var notification = await _repository.GetByIdAsync(new NotificationId(command.NotificationId), cancellationToken);

        // Idempotent : déjà supprimée = succès (un balayage ne doit jamais échouer
        // pour une notif déjà partie).
        if (notification is null)
        {
            return Result.Success();
        }

        // SÉCURITÉ : on ne supprime QUE ses propres notifications. On renvoie
        // « introuvable » (et non « interdit ») pour ne pas révéler son existence.
        if (notification.RecipientUserId != command.RecipientUserId)
        {
            return Result.Failure(Error.NotFound("notifications.not_found", "Notification introuvable."));
        }

        _repository.Remove(notification);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

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
