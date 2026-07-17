using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Messaging.Domain.Conversations;

/// <summary>
/// Message d'une conversation, avec pièces jointes éventuelles (URLs object
/// storage), accusé de lecture, réactions et suppression. Entité enfant de Conversation.
///
/// Deux formes de suppression, comme dans WhatsApp :
/// <list type="bullet">
///   <item><b>Pour tout le monde</b> (<see cref="DeletedAtUtc"/>) — réservée à l'auteur.
///     Le corps N'EST PAS effacé de la base : il reste disponible pour le support et
///     la preuve en cas de litige (un vendeur ne doit pas pouvoir effacer un engagement
///     écrit). C'est la <b>projection</b> qui le remplace par « Message supprimé ».</item>
///   <item><b>Pour moi</b> (<see cref="HiddenFor"/>) — n'importe quel participant masque
///     le message de SA vue ; l'autre continue de le voir.</item>
/// </list>
/// </summary>
public sealed class Message : Entity<Guid>
{
    private readonly List<string> _attachments = new();
    private readonly List<MessageReaction> _reactions = new();
    private readonly List<MessageHiddenFor> _hiddenFor = new();

    private Message()
    {
    }

    internal Message(Guid id, Guid senderId, string body, IEnumerable<string> attachments)
        : base(id)
    {
        SenderId = senderId;
        Body = body;
        _attachments.AddRange(attachments.Where(a => !string.IsNullOrWhiteSpace(a)));
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid SenderId { get; private set; }
    public string Body { get; private set; } = default!;
    public IReadOnlyCollection<string> Attachments => _attachments.AsReadOnly();
    public DateTime? ReadAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Date de suppression « pour tout le monde ». Null = message actif.</summary>
    public DateTime? DeletedAtUtc { get; private set; }

    public bool IsDeleted => DeletedAtUtc is not null;

    public IReadOnlyCollection<MessageReaction> Reactions => _reactions.AsReadOnly();
    public IReadOnlyCollection<MessageHiddenFor> HiddenFor => _hiddenFor.AsReadOnly();

    internal void MarkRead() => ReadAtUtc ??= DateTime.UtcNow;

    /// <summary>Suppression pour tout le monde. Idempotent. Le corps est conservé en base.</summary>
    internal void DeleteForEveryone() => DeletedAtUtc ??= DateTime.UtcNow;

    /// <summary>Masque le message pour un utilisateur donné (« supprimer pour moi »). Idempotent.</summary>
    internal void HideFor(Guid userId)
    {
        if (_hiddenFor.All(h => h.UserId != userId))
        {
            _hiddenFor.Add(new MessageHiddenFor(Guid.NewGuid(), userId));
        }
    }

    public bool IsHiddenFor(Guid userId) => _hiddenFor.Any(h => h.UserId == userId);

    /// <summary>
    /// Applique une réaction : ajoute si l'utilisateur n'en a pas, la RETIRE si c'est
    /// le même emoji (bascule), la REMPLACE sinon. Une seule réaction par personne.
    /// </summary>
    internal void React(Guid userId, string emoji)
    {
        var existing = _reactions.FirstOrDefault(r => r.UserId == userId);
        if (existing is null)
        {
            _reactions.Add(new MessageReaction(Guid.NewGuid(), userId, emoji));
            return;
        }

        if (string.Equals(existing.Emoji, emoji, StringComparison.Ordinal))
        {
            _reactions.Remove(existing); // même emoji → on retire (bascule)
            return;
        }

        existing.ChangeEmoji(emoji);
    }
}

/// <summary>Marqueur « ce message est masqué pour cet utilisateur » (suppression pour moi).</summary>
public sealed class MessageHiddenFor : Entity<Guid>
{
    private MessageHiddenFor()
    {
    }

    internal MessageHiddenFor(Guid id, Guid userId)
        : base(id)
    {
        UserId = userId;
        HiddenAtUtc = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }
    public DateTime HiddenAtUtc { get; private set; }
}
