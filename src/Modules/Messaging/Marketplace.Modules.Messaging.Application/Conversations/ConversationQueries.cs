using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Messaging.Contracts;
using Marketplace.Modules.Messaging.Domain.Conversations;

namespace Marketplace.Modules.Messaging.Application.Conversations;

/// <summary>Récupère une conversation (le demandeur doit en être participant).</summary>
public sealed record GetConversationQuery(Guid ConversationId, Guid RequesterId) : IQuery<ConversationSummary>;

/// <summary>Liste les conversations de l'utilisateur.</summary>
public sealed record ListMyConversationsQuery(Guid UserId) : IQuery<IReadOnlyList<ConversationSummary>>;

internal static class ConversationMapper
{
    /// <summary>Corps affiché à la place d'un message supprimé pour tout le monde.</summary>
    private const string DeletedPlaceholder = "Message supprimé";

    /// <summary>
    /// Projette une conversation POUR UN LECTEUR donné. C'est ici — et nulle part
    /// ailleurs — que s'applique la confidentialité :
    /// <list type="bullet">
    ///   <item>les messages que le lecteur a masqués (« supprimer pour moi ») sont
    ///         purement et simplement absents de sa vue ;</item>
    ///   <item>les messages supprimés pour tout le monde voient leur corps ET leurs
    ///         pièces jointes remplacés par un marqueur — le contenu réel reste en base
    ///         pour le support et la preuve, mais ne sort jamais par cette API ;</item>
    ///   <item>les réactions sont agrégées par emoji, avec un drapeau « Mine ».</item>
    /// </list>
    /// </summary>
    public static ConversationSummary ToSummary(Conversation c, Guid viewerId) => new(
        c.Id.Value, c.ParticipantIds.ToList(), c.ContextType, c.ContextId, c.Status.ToString(), c.LastMessageAtUtc,
        c.Messages
            .Where(m => !m.IsHiddenFor(viewerId))
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new MessageSummary(
                m.Id,
                m.SenderId,
                m.IsDeleted ? DeletedPlaceholder : m.Body,
                m.IsDeleted ? Array.Empty<string>() : m.Attachments.Select(a => a.Url).ToList(),
                m.ReadAtUtc,
                m.CreatedAtUtc,
                m.IsDeleted,
                m.IsDeleted
                    ? Array.Empty<MessageReactionSummary>()
                    : m.Reactions
                        .GroupBy(r => r.Emoji, StringComparer.Ordinal)
                        .Select(g => new MessageReactionSummary(g.Key, g.Count(), g.Any(r => r.UserId == viewerId)))
                        .ToList()))
            .ToList());
}

internal sealed class GetConversationQueryHandler : IQueryHandler<GetConversationQuery, ConversationSummary>
{
    private readonly IConversationRepository _repository;
    public GetConversationQueryHandler(IConversationRepository repository) => _repository = repository;

    public async Task<Result<ConversationSummary>> Handle(GetConversationQuery query, CancellationToken cancellationToken)
    {
        var conversation = await _repository.GetByIdAsync(new ConversationId(query.ConversationId), cancellationToken);
        if (conversation is null)
        {
            return Error.NotFound("messaging.not_found", "Conversation introuvable.");
        }

        if (!conversation.ParticipantIds.Contains(query.RequesterId))
        {
            return Error.Forbidden("messaging.not_participant", "Vous n'êtes pas participant à cette conversation.");
        }

        return ConversationMapper.ToSummary(conversation, query.RequesterId);
    }
}

internal sealed class ListMyConversationsQueryHandler : IQueryHandler<ListMyConversationsQuery, IReadOnlyList<ConversationSummary>>
{
    private readonly IConversationRepository _repository;
    public ListMyConversationsQueryHandler(IConversationRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ConversationSummary>>> Handle(ListMyConversationsQuery query, CancellationToken cancellationToken)
    {
        var items = await _repository.ListByParticipantAsync(query.UserId, cancellationToken);
        IReadOnlyList<ConversationSummary> summaries = items.Select(c => ConversationMapper.ToSummary(c, query.UserId)).ToList();
        return Result.Success(summaries);
    }
}
