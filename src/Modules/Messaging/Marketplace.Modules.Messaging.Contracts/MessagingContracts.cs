namespace Marketplace.Modules.Messaging.Contracts;

/// <summary>
/// Réaction agrégée sur un message : l'emoji, combien de personnes l'ont mis, et si
/// le lecteur courant en fait partie (pour afficher la pastille en surbrillance).
/// </summary>
public sealed record MessageReactionSummary(string Emoji, int Count, bool Mine);

/// <summary>
/// Message d'une conversation, projeté POUR UN LECTEUR donné.
/// <para>
/// <c>IsDeleted</c> = supprimé pour tout le monde : le <c>Body</c> renvoyé vaut alors
/// « Message supprimé » (le corps réel reste en base, accessible au support).
/// Les messages masqués par le lecteur (« supprimer pour moi ») ne sont pas renvoyés du tout.
/// </para>
/// </summary>
public sealed record MessageSummary(
    Guid Id,
    Guid SenderId,
    string Body,
    IReadOnlyList<string> Attachments,
    DateTime? ReadAtUtc,
    DateTime CreatedAtUtc,
    bool IsDeleted,
    IReadOnlyList<MessageReactionSummary> Reactions);

/// <summary>Vue publique d'une conversation (projetée pour un lecteur).</summary>
public sealed record ConversationSummary(
    Guid Id,
    IReadOnlyList<Guid> ParticipantIds,
    string? ContextType,
    Guid? ContextId,
    string Status,
    DateTime LastMessageAtUtc,
    IReadOnlyList<MessageSummary> Messages);
