using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Messaging.Application.Abstractions;
using Marketplace.Modules.Messaging.Domain.Conversations;

namespace Marketplace.Modules.Messaging.Application.Conversations;

/// <summary>Démarre une conversation avec un destinataire (contexte produit/commande optionnel).</summary>
public sealed record StartConversationCommand(
    Guid StarterId, Guid RecipientId, string? ContextType, Guid? ContextId, string Message) : ICommand<Guid>;

/// <summary>Envoie un message dans une conversation.</summary>
public sealed record SendMessageCommand(Guid ConversationId, Guid SenderId, string Body, IReadOnlyList<string>? Attachments) : ICommand;

/// <summary>Marque comme lus les messages reçus dans une conversation.</summary>
public sealed record MarkConversationReadCommand(Guid ConversationId, Guid ReaderId) : ICommand;

/// <summary>Archive une conversation (participant).</summary>
public sealed record ArchiveConversationCommand(Guid ConversationId, Guid UserId) : ICommand;

public sealed class StartConversationCommandValidator : AbstractValidator<StartConversationCommand>
{
    public StartConversationCommandValidator()
    {
        RuleFor(c => c.StarterId).NotEmpty();
        RuleFor(c => c.RecipientId).NotEmpty().NotEqual(c => c.StarterId).WithMessage("Le destinataire doit différer de l'expéditeur.");
        RuleFor(c => c.Message).NotEmpty().MaximumLength(4000);
    }
}

public sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(c => c.ConversationId).NotEmpty();
        RuleFor(c => c.SenderId).NotEmpty();
        RuleFor(c => c.Body).NotEmpty().MaximumLength(4000);
    }
}

internal sealed class StartConversationCommandHandler : ICommandHandler<StartConversationCommand, Guid>
{
    private readonly IConversationRepository _repository;
    private readonly IMessagingUnitOfWork _unitOfWork;

    public StartConversationCommandHandler(IConversationRepository repository, IMessagingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(StartConversationCommand command, CancellationToken cancellationToken)
    {
        var result = Conversation.Start(
            new[] { command.StarterId, command.RecipientId }, command.ContextType, command.ContextId, command.StarterId, command.Message);

        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id.Value;
    }
}

internal abstract class ConversationMutationHandlerBase
{
    protected readonly IConversationRepository Repository;
    protected readonly IMessagingUnitOfWork UnitOfWork;

    protected ConversationMutationHandlerBase(IConversationRepository repository, IMessagingUnitOfWork unitOfWork)
    {
        Repository = repository;
        UnitOfWork = unitOfWork;
    }

    protected async Task<Result> MutateAsync(Guid conversationId, Func<Conversation, Result> mutate, CancellationToken ct)
    {
        var conversation = await Repository.GetByIdAsync(new ConversationId(conversationId), ct);
        if (conversation is null)
        {
            return Result.Failure(Error.NotFound("messaging.not_found", "Conversation introuvable."));
        }

        var result = mutate(conversation);
        if (result.IsFailure)
        {
            return result;
        }

        await UnitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class SendMessageCommandHandler : ConversationMutationHandlerBase, ICommandHandler<SendMessageCommand>
{
    public SendMessageCommandHandler(IConversationRepository r, IMessagingUnitOfWork u) : base(r, u) { }

    public Task<Result> Handle(SendMessageCommand c, CancellationToken ct)
        => MutateAsync(c.ConversationId, conv => conv.SendMessage(c.SenderId, c.Body, c.Attachments ?? Array.Empty<string>()), ct);
}

internal sealed class MarkConversationReadCommandHandler : ConversationMutationHandlerBase, ICommandHandler<MarkConversationReadCommand>
{
    public MarkConversationReadCommandHandler(IConversationRepository r, IMessagingUnitOfWork u) : base(r, u) { }

    public Task<Result> Handle(MarkConversationReadCommand c, CancellationToken ct)
        => MutateAsync(c.ConversationId, conv => conv.MarkRead(c.ReaderId), ct);
}

internal sealed class ArchiveConversationCommandHandler : ConversationMutationHandlerBase, ICommandHandler<ArchiveConversationCommand>
{
    public ArchiveConversationCommandHandler(IConversationRepository r, IMessagingUnitOfWork u) : base(r, u) { }

    public Task<Result> Handle(ArchiveConversationCommand c, CancellationToken ct)
        => MutateAsync(c.ConversationId, conv =>
        {
            if (!conv.ParticipantIds.Contains(c.UserId))
            {
                return Result.Failure(Error.Forbidden("messaging.not_participant", "Vous n'êtes pas participant."));
            }

            conv.Archive();
            return Result.Success();
        }, ct);
}
