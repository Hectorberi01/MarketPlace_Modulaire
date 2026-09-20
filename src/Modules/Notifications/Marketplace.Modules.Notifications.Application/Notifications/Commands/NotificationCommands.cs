using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Domain.Devices;
using Marketplace.Modules.Notifications.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace Marketplace.Modules.Notifications.Application.Notifications.Commands;

/// <summary>Marque une notification comme lue (seul son destinataire le peut).</summary>
public sealed record MarkNotificationReadCommand(Guid NotificationId, Guid RecipientUserId) : ICommand;

/// <summary>Marque toutes les notifications de l'utilisateur comme lues.</summary>
public sealed record MarkAllNotificationsReadCommand(Guid RecipientUserId) : ICommand;

/// <summary>Envoie une notification à un utilisateur (back-office admin).</summary>
public sealed record SendNotificationCommand(Guid RecipientUserId, string? Channel, string Subject, string Body) : ICommand<Guid>;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// DIFFUSER UNE ANNONCE AUX UTILISATEURS JOIGNABLES PAR PUSH.
///
/// C'est le pendant notifié du bandeau d'annonce : la même information, poussée sur le
/// téléphone de ceux qui ont installé l'application. Le bandeau reste le canal principal
/// — il touche tout le monde, y compris le web, et n'a besoin d'aucune autorisation.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// TROIS GARDE-FOUS, ET AUCUN N'EST DÉCORATIF.
///
/// 1. CATÉGORIE COUPABLE. La diffusion passe par `relatedType = "announcement"`, qui
///    est mappé sur la catégorie « announcements ». Un utilisateur qui l'a coupée ne
///    reçoit pas le push. Sans ce mappage, `FromRelatedType` aurait rendu `null` et le
///    dispatcher lit `null` comme « rien ne s'y oppose ».
///
/// 2. PLAFOND EXPLICITE. `Max` borne le nombre de destinataires, et il n'a pas de
///    valeur par défaut généreuse : une diffusion est une action dont on doit connaître
///    la portée AVANT de la lancer. Une erreur de manipulation qui touche cinquante
///    mille personnes ne se rattrape pas — un push parti est parti.
///
/// 3. AUDIENCE BORNÉE AU MODULE. Les destinataires sont ceux qui ont enregistré un
///    appareil, pas « tous les comptes ». Voir `IDeviceTokenRepository.ListUserIdsAsync`.
///
/// CE N'EST PAS UN OUTIL DE PROSPECTION. Rien ici ne segmente par comportement d'achat,
/// et c'est délibéré : le jour où cela deviendra nécessaire, ce sera une décision à
/// prendre avec le texte d'information des utilisateurs sous les yeux, pas un paramètre
/// de plus ajouté en passant.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record BroadcastAnnouncementCommand(
    string Subject, string Body, int Max, string? Platform) : ICommand<int>;

/// <summary>Supprime une notification (back-office admin, sans contrôle de propriété).</summary>
public sealed record DeleteNotificationCommand(Guid NotificationId) : ICommand;

/// <summary>Supprime SA propre notification (balayage côté app). Vérifie le destinataire.</summary>
public sealed record DeleteOwnNotificationCommand(Guid NotificationId, Guid RecipientUserId) : ICommand;

internal sealed class BroadcastAnnouncementCommandHandler : ICommandHandler<BroadcastAnnouncementCommand, int>
{
    /// <summary>
    /// Plafond absolu, au-dessus du plafond demandé.
    ///
    /// Il ne protège pas contre une diffusion voulue — celle-là passe par `Max` — mais
    /// contre une diffusion dont personne n'a mesuré la taille : un `Max` saisi à
    /// 1 000 000 par glissement de doigt, ou recopié d'un script. Au-delà, la commande
    /// refuse plutôt que de tronquer en silence, parce qu'une troncature silencieuse
    /// laisserait croire que tout le monde a reçu le message.
    ///
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// 500, ET PAS 5 000 : CE TRAVAIL SE FAIT DANS LA REQUÊTE HTTP DE L'ADMINISTRATEUR.
    ///
    /// Chaque destinataire coûte deux lectures en base — ses préférences, ses appareils
    /// — et un aller-retour vers FCM. À 5 000, cela faisait plus de dix mille requêtes
    /// SQL et cinq mille appels réseau avant la première ligne de réponse : la requête
    /// expirait, et l'administrateur restait sans savoir ce qui était parti. Il aurait
    /// alors relancé.
    ///
    /// 500 tient en quelques dizaines de secondes. Au-delà, il faudra sortir l'envoi de
    /// la requête — une file et un service d'arrière-plan, comme l'outbox — et ce sera
    /// un travail en soi, pas une constante qu'on augmente.
    /// ═══════════════════════════════════════════════════════════════════════════════
    private const int PlafondAbsolu = 500;

    private readonly IDeviceTokenRepository _deviceTokens;
    private readonly INotificationRepository _repository;
    private readonly INotificationsUnitOfWork _unitOfWork;
    private readonly NotificationDispatcher _dispatcher;
    private readonly ILogger<BroadcastAnnouncementCommandHandler> _logger;

    public BroadcastAnnouncementCommandHandler(
        IDeviceTokenRepository deviceTokens,
        INotificationRepository repository,
        INotificationsUnitOfWork unitOfWork,
        NotificationDispatcher dispatcher,
        ILogger<BroadcastAnnouncementCommandHandler> logger)
    {
        _deviceTokens = deviceTokens;
        _repository = repository;
        _unitOfWork = unitOfWork;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(BroadcastAnnouncementCommand command, CancellationToken cancellationToken)
    {
        if (command.Max is < 1 or > PlafondAbsolu)
        {
            return Result.Failure<int>(Error.Validation(
                "notifications.broadcast.max_invalid",
                $"Le nombre de destinataires doit être compris entre 1 et {PlafondAbsolu}."));
        }

        var destinataires = await _deviceTokens.ListUserIdsAsync(command.Platform, command.Max, cancellationToken);
        if (destinataires.Count == 0)
        {
            return Result.Success(0);
        }

        // LES NOTIFICATIONS IN-APP D'ABORD, EN UN SEUL ENREGISTREMENT. Elles constituent
        // la trace : ce qui a été dit, à qui, et quand. Les écrire une par une aurait
        // multiplié les allers-retours par le nombre de destinataires.
        foreach (var userId in destinataires)
        {
            var creation = Notification.Create(
                userId, NotificationChannel.InApp, command.Subject, command.Body, "Announcement", null);
            if (creation.IsFailure)
            {
                return Result.Failure<int>(creation.Error);
            }

            var notification = creation.Value;
            notification.MarkSent();
            await _repository.AddAsync(notification, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // LE PUSH ENSUITE, ET SON ÉCHEC N'ANNULE RIEN. La trace est déjà enregistrée ;
        // le dispatcher, lui, respecte la coupure de catégorie utilisateur par
        // utilisateur — « Announcement » est mappé sur « announcements ».
        //
        // PAS DE `try/catch` ICI, ET PAS DE COMPTEUR D'ENVOIS. `SendPushAsync` attrape
        // déjà toutes ses exceptions et rend normalement : un `catch` autour serait du
        // code mort, et un compteur incrémenté après l'appel aurait valu le nombre de
        // destinataires quoi qu'il arrive — y compris pour ceux qui ont coupé la
        // catégorie ou n'ont aucun appareil. Un journal qui compte toujours juste ne
        // compte rien. Le détail par utilisateur est déjà tracé par le dispatcher.
        foreach (var userId in destinataires)
        {
            await _dispatcher.SendPushAsync(
                userId, command.Subject, command.Body, "Announcement", null, cancellationToken);
        }

        _logger.LogInformation(
            "Diffusion « {Subject} » : {Recipients} destinataire(s) notifié(s).",
            command.Subject, destinataires.Count);

        return Result.Success(destinataires.Count);
    }
}

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

        // « INTROUVABLE », PAS « INTERDIT » — LA MÊME RÈGLE QUE LE DELETE DE CE FICHIER.
        //
        // `DeleteOwnNotificationCommandHandler`, trente lignes plus haut, répond déjà
        // `notifications.not_found` sur exactement le même prédicat, avec la raison en
        // commentaire. Ici on répondait 403 : deux handlers voisins, une seule
        // condition, deux décisions opposées. Celle-ci révélait qu'une notification
        // existe — donc qu'il s'est passé quelque chose pour quelqu'un d'autre.
        //
        // Le code d'erreur est repris tel quel, pas seulement le statut : `ApiResults`
        // le publie dans le `title` du ProblemDetails, où `notifications.not_owner`
        // aurait dit tout haut ce que le 404 taisait.
        if (notification.RecipientUserId != command.RecipientUserId)
        {
            return Result.Failure(Error.NotFound("notifications.not_found", "Notification introuvable."));
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
