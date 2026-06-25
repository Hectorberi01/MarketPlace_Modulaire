using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Messaging.Domain.Conversations;

/// <summary>
/// Message d'une conversation, avec pièces jointes éventuelles (URLs object
/// storage) et accusé de lecture. Entité enfant de Conversation.
/// </summary>
public sealed class Message : Entity<Guid>
{
    private List<string> _attachments = new();

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

    internal void MarkRead() => ReadAtUtc ??= DateTime.UtcNow;
}
