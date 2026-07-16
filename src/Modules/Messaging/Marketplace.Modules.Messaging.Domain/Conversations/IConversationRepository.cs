namespace Marketplace.Modules.Messaging.Domain.Conversations;

public interface IConversationRepository
{
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default);

    Task<Conversation?> GetByIdAsync(ConversationId id, CancellationToken cancellationToken = default);

    /// <summary>Conversations où l'utilisateur est participant (les plus récentes d'abord).</summary>
    Task<IReadOnlyList<Conversation>> ListByParticipantAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cet utilisateur est-il participant de cette conversation ?
    ///
    /// EXISTS pur : ni Include, ni matérialisation. `GetByIdAsync` chargerait la
    /// conversation ENTIÈRE — tous les messages, toutes les réactions, tous les masquages —
    /// pour ne répondre qu'à une question booléenne, et à chaque ouverture de fil.
    /// </summary>
    Task<bool> IsParticipantAsync(ConversationId id, Guid userId, CancellationToken cancellationToken = default);
}
