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

    /// <summary>
    /// LE REFUS EST « INTROUVABLE », PAS « INTERDIT ». ET LE CODE COMPTE AUTANT QUE LE
    /// STATUT.
    ///
    /// Ces méthodes répondaient 403 `messaging.not_participant` quand l'appelant n'est
    /// pas membre, et 404 `messaging.not_found` quand la conversation n'existe pas. Deux
    /// réponses distinctes pour deux situations qu'un inconnu ne doit pas savoir
    /// distinguer : il suffisait d'essayer des identifiants un par un pour cartographier
    /// les conversations réelles de la plateforme.
    ///
    /// Le piège, c'est que passer en 404 sans changer le CODE n'aurait rien réglé :
    /// `ApiResults` place le code d'erreur dans le champ `title` du ProblemDetails. Un
    /// 404 `messaging.not_participant` dit exactement ce qu'il prétendait taire. Le code
    /// ET le message sont donc ceux, mot pour mot, du cas « conversation inexistante »
    /// produit par `ConversationMutationHandlerBase.MutateAsync`.
    ///
    /// C'est la règle que `ChatHub.JoinConversation` applique depuis toujours — même
    /// exception pour un GUID invalide et pour une conversation dont on n'est pas membre.
    /// Le module la respecte enfin partout.
    ///
    /// À NE PAS CONFONDRE avec `messaging.not_author` plus bas, qui reste un 403 : là,
    /// l'appelant EST participant, il voit le message, et lui dire qu'il n'en est pas
    /// l'auteur ne lui apprend rien qu'il ne puisse déjà lire.
    /// </summary>
    private static Error NotAParticipant => Error.NotFound("messaging.not_found", "Conversation introuvable.");

    public Result SendMessage(Guid senderId, string body, IEnumerable<string> attachments)
    {
        // LE CONTRÔLE DE PARTICIPATION PASSE EN PREMIER, ET CE N'EST PAS COSMÉTIQUE.
        //
        // Il venait après le contrôle d'ouverture : un inconnu apprenait donc si une
        // conversation est ouverte ou close avant qu'on lui refuse l'accès. Deux
        // réponses différentes pour deux conversations dont il n'est membre d'aucune,
        // c'est déjà de l'information.
        if (_participants.All(p => p.UserId != senderId))
        {
            return Result.Failure(NotAParticipant);
        }

        if (Status != ConversationStatus.Open)
        {
            return Result.Failure(Error.Conflict("messaging.not_open", "La conversation n'est pas ouverte."));
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
            return Result.Failure(NotAParticipant);
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
            return Result.Failure(NotAParticipant);
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
            return Result.Failure(NotAParticipant);
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
            return Result.Failure(NotAParticipant);
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
