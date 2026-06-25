namespace Marketplace.Modules.Messaging.Domain.Conversations;

/// <summary>Identité forte d'une conversation.</summary>
public readonly record struct ConversationId(Guid Value)
{
    public static ConversationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Statut d'un fil de discussion.</summary>
public enum ConversationStatus
{
    Open = 0,
    Archived = 1,
    Blocked = 2
}
