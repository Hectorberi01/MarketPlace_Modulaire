using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Messaging.Domain.Conversations.Events;

namespace Marketplace.Modules.Messaging.Domain.Conversations;

/// <summary>
/// Fil de discussion entre participants (acheteur ↔ vendeur, ou support),
/// optionnellement rattaché à un contexte (produit, commande). Agrégat racine :
/// possède ses messages.
/// </summary>
public sealed class Conversation : AggregateRoot<ConversationId>
{
    private readonly List<ConversationParticipant> _participants = new();
    private readonly List<Message> _messages = new();

    private Conversation()
    {
    }

    private Conversation(ConversationId id, IEnumerable<Guid> participantIds, string? contextType, Guid? contextId)
        : base(id)
    {
        foreach (var userId in participantIds.Distinct())
        {
            _participants.Add(new ConversationParticipant(Guid.NewGuid(), userId));
        }

        ContextType = contextType;
        ContextId = contextId;
        Status = ConversationStatus.Open;
        LastMessageAtUtc = DateTime.UtcNow;
    }

    public IReadOnlyCollection<ConversationParticipant> Participants => _participants.AsReadOnly();
    public IReadOnlyCollection<Guid> ParticipantIds => _participants.Select(p => p.UserId).ToList().AsReadOnly();
    public string? ContextType { get; private set; }
    public Guid? ContextId { get; private set; }
    public ConversationStatus Status { get; private set; }
    public DateTime LastMessageAtUtc { get; private set; }
    public IReadOnlyCollection<Message> Messages => _messages.AsReadOnly();

    public static Result<Conversation> Start(
        IEnumerable<Guid> participantIds, string? contextType, Guid? contextId, Guid senderId, string initialBody)
    {
        var participants = participantIds?.Where(p => p != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (participants.Count < 2)
        {
            return Error.Validation("messaging.participants_required", "Une conversation requiert au moins deux participants.");
        }

        if (!participants.Contains(senderId))
        {
            return Error.Validation("messaging.sender_not_participant", "L'expéditeur doit être un participant.");
        }

        if (string.IsNullOrWhiteSpace(initialBody))
        {
            return Error.Validation("messaging.body_required", "Le message initial est obligatoire.");
        }

        var conversation = new Conversation(ConversationId.New(), participants, contextType, contextId);
        conversation._messages.Add(new Message(Guid.NewGuid(), senderId, initialBody.Trim(), Array.Empty<string>()));
        conversation.Raise(new ConversationStartedDomainEvent(conversation.Id.Value, senderId));
        return conversation;
    }

    public Result SendMessage(Guid senderId, string body, IEnumerable<string> attachments)
    {
        if (Status != ConversationStatus.Open)
        {
            return Result.Failure(Error.Conflict("messaging.not_open", "La conversation n'est pas ouverte."));
        }

        if (_participants.All(p => p.UserId != senderId))
        {
            return Result.Failure(Error.Forbidden("messaging.not_participant", "Vous n'êtes pas participant à cette conversation."));
        }

        // Une photo sans légende est un message légitime : on n'exige un corps de
        // texte QUE s'il n'y a aucune pièce jointe. On refuse seulement le message
        // totalement vide (ni texte, ni pièce jointe).
        var hasAttachment = attachments.Any(a => !string.IsNullOrWhiteSpace(a));
        if (string.IsNullOrWhiteSpace(body) && !hasAttachment)
        {
            return Result.Failure(Error.Validation(
                "messaging.body_required", "Un message doit contenir du texte ou au moins une pièce jointe."));
        }

        var message = new Message(Guid.NewGuid(), senderId, (body ?? string.Empty).Trim(), attachments ?? Array.Empty<string>());
        _messages.Add(message);
        LastMessageAtUtc = message.CreatedAtUtc;
        Raise(new MessageSentDomainEvent(Id.Value, message.Id, senderId));
        return Result.Success();
    }

    /// <summary>Marque comme lus les messages reçus par le lecteur (non envoyés par lui).</summary>
    public Result MarkRead(Guid readerId)
    {
        if (_participants.All(p => p.UserId != readerId))
        {
            return Result.Failure(Error.Forbidden("messaging.not_participant", "Vous n'êtes pas participant à cette conversation."));
        }

        foreach (var message in _messages.Where(m => m.SenderId != readerId && m.ReadAtUtc is null))
        {
            message.MarkRead();
        }

        return Result.Success();
    }

    /// <summary>
    /// Réagit à un message (emoji de la palette autorisée). Une seule réaction par
    /// personne : re-cliquer le même emoji la retire, en cliquer un autre la remplace.
    /// </summary>
    public Result ReactToMessage(Guid messageId, Guid userId, string emoji)
    {
        if (_participants.All(p => p.UserId != userId))
        {
            return Result.Failure(Error.Forbidden("messaging.not_participant", "Vous n'êtes pas participant à cette conversation."));
        }

        if (!MessageReactions.IsAllowed(emoji))
        {
            return Result.Failure(Error.Validation("messaging.reaction_not_allowed", "Cet emoji n'est pas autorisé."));
        }

        var message = _messages.FirstOrDefault(m => m.Id == messageId);
        if (message is null)
        {
            return Result.Failure(Error.NotFound("messaging.message_not_found", "Message introuvable."));
        }

        if (message.IsDeleted)
        {
            return Result.Failure(Error.Conflict("messaging.message_deleted", "Impossible de réagir à un message supprimé."));
        }

        message.React(userId, emoji);
        return Result.Success();
    }

    /// <summary>
    /// Supprime un message POUR TOUT LE MONDE. Réservé à l'auteur : on ne peut pas
    /// effacer la parole d'autrui. Le corps reste stocké (preuve/support) ; c'est la
    /// projection qui affiche « Message supprimé ». Idempotent.
    /// </summary>
    public Result DeleteMessageForEveryone(Guid messageId, Guid userId)
    {
        if (_participants.All(p => p.UserId != userId))
        {
            return Result.Failure(Error.Forbidden("messaging.not_participant", "Vous n'êtes pas participant à cette conversation."));
        }

        var message = _messages.FirstOrDefault(m => m.Id == messageId);
        if (message is null)
        {
            return Result.Failure(Error.NotFound("messaging.message_not_found", "Message introuvable."));
        }

        if (message.SenderId != userId)
        {
            return Result.Failure(Error.Forbidden("messaging.not_author", "Vous ne pouvez supprimer que vos propres messages."));
        }

        message.DeleteForEveryone();
        return Result.Success();
    }

    /// <summary>
    /// Masque un message POUR CE PARTICIPANT UNIQUEMENT (« supprimer pour moi »).
    /// N'importe quel message du fil, y compris ceux reçus. L'autre continue de le voir.
    /// Idempotent.
    /// </summary>
    public Result HideMessageForUser(Guid messageId, Guid userId)
    {
        if (_participants.All(p => p.UserId != userId))
        {
            return Result.Failure(Error.Forbidden("messaging.not_participant", "Vous n'êtes pas participant à cette conversation."));
        }

        var message = _messages.FirstOrDefault(m => m.Id == messageId);
        if (message is null)
        {
            return Result.Failure(Error.NotFound("messaging.message_not_found", "Message introuvable."));
        }

        message.HideFor(userId);
        return Result.Success();
    }

    public void Archive() => Status = ConversationStatus.Archived;

    public void Block() => Status = ConversationStatus.Blocked;
}
