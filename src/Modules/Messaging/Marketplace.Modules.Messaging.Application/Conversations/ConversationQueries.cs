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
    public static ConversationSummary ToSummary(Conversation c) => new(
        c.Id.Value, c.ParticipantIds.ToList(), c.ContextType, c.ContextId, c.Status.ToString(), c.LastMessageAtUtc,
        c.Messages.OrderBy(m => m.CreatedAtUtc)
            .Select(m => new MessageSummary(m.Id, m.SenderId, m.Body, m.Attachments.ToList(), m.ReadAtUtc, m.CreatedAtUtc)).ToList());
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

        return ConversationMapper.ToSummary(conversation);
    }
}

internal sealed class ListMyConversationsQueryHandler : IQueryHandler<ListMyConversationsQuery, IReadOnlyList<ConversationSummary>>
{
    private readonly IConversationRepository _repository;
    public ListMyConversationsQueryHandler(IConversationRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ConversationSummary>>> Handle(ListMyConversationsQuery query, CancellationToken cancellationToken)
    {
        var items = await _repository.ListByParticipantAsync(query.UserId, cancellationToken);
        IReadOnlyList<ConversationSummary> summaries = items.Select(ConversationMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
