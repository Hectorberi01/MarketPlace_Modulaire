using Marketplace.Modules.Messaging.Contracts;
using Marketplace.Modules.Messaging.Domain.Conversations;

namespace Marketplace.Modules.Messaging.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Messaging.</summary>
internal sealed class MessagingModuleApi : IMessagingModuleApi
{
    private readonly IConversationRepository _conversations;

    public MessagingModuleApi(IConversationRepository conversations)
        => _conversations = conversations;

    public Task<bool> IsParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default)
        => _conversations.IsParticipantAsync(new ConversationId(conversationId), userId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListParticipantsAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetByIdAsync(new ConversationId(conversationId), cancellationToken);
        return conversation is null ? Array.Empty<Guid>() : conversation.ParticipantIds.ToList();
    }
}
