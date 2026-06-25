namespace Marketplace.Modules.Messaging.Contracts;

/// <summary>Message d'une conversation.</summary>
public sealed record MessageSummary(
    Guid Id, Guid SenderId, string Body, IReadOnlyList<string> Attachments, DateTime? ReadAtUtc, DateTime CreatedAtUtc);

/// <summary>Vue publique d'une conversation.</summary>
public sealed record ConversationSummary(
    Guid Id,
    IReadOnlyList<Guid> ParticipantIds,
    string? ContextType,
    Guid? ContextId,
    string Status,
    DateTime LastMessageAtUtc,
    IReadOnlyList<MessageSummary> Messages);
