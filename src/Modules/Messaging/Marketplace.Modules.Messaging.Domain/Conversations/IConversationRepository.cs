namespace Marketplace.Modules.Messaging.Domain.Conversations;

public interface IConversationRepository
{
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default);

    Task<Conversation?> GetByIdAsync(ConversationId id, CancellationToken cancellationToken = default);

    /// <summary>Conversations où l'utilisateur est participant (les plus récentes d'abord).</summary>
    Task<IReadOnlyList<Conversation>> ListByParticipantAsync(Guid userId, CancellationToken cancellationToken = default);
}
