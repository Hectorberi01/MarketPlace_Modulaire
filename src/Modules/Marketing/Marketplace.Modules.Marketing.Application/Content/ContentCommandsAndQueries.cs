using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Marketing.Application.Abstractions;
using Marketplace.Modules.Marketing.Contracts;
using Marketplace.Modules.Marketing.Domain.Content;

namespace Marketplace.Modules.Marketing.Application.Content;

/// <summary>Crée un bloc de contenu (brouillon).</summary>
public sealed record CreateContentBlockCommand(string Type, string Slot, int Position, string Content, DateTime? StartUtc, DateTime? EndUtc) : ICommand<Guid>;

/// <summary>Publie un bloc de contenu.</summary>
public sealed record PublishContentBlockCommand(Guid ContentBlockId) : ICommand;

/// <summary>Archive un bloc de contenu.</summary>
public sealed record ArchiveContentBlockCommand(Guid ContentBlockId) : ICommand;

/// <summary>Liste les blocs publiés d'un slot (rendu vitrine).</summary>
public sealed record ListContentBySlotQuery(string Slot) : IQuery<IReadOnlyList<ContentBlockSummary>>;

public sealed class CreateContentBlockCommandValidator : AbstractValidator<CreateContentBlockCommand>
{
    public CreateContentBlockCommandValidator()
    {
        RuleFor(c => c.Type).Must(v => v is "Banner" or "Page" or "FeaturedList" or "Faq").WithMessage("Type de bloc invalide.");
        RuleFor(c => c.Slot).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Content).NotEmpty();
    }
}

internal static class ContentMapper
{
    public static ContentBlockSummary ToSummary(ContentBlock b) => new(
        b.Id.Value, b.Type.ToString(), b.Slot, b.Position, b.Content, b.StartUtc, b.EndUtc, b.Status.ToString());
}

internal sealed class CreateContentBlockCommandHandler : ICommandHandler<CreateContentBlockCommand, Guid>
{
    private readonly IContentBlockRepository _repository;
    private readonly IMarketingUnitOfWork _unitOfWork;

    public CreateContentBlockCommandHandler(IContentBlockRepository repository, IMarketingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateContentBlockCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ContentBlockType>(command.Type, ignoreCase: true, out var type))
        {
            return Result.Failure<Guid>(Error.Validation("marketing.content.type_invalid", "Type de bloc inconnu."));
        }

        var result = ContentBlock.Create(type, command.Slot, command.Position, command.Content, command.StartUtc, command.EndUtc);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id.Value;
    }
}

internal abstract class ContentMutationHandlerBase
{
    protected readonly IContentBlockRepository Repository;
    protected readonly IMarketingUnitOfWork UnitOfWork;

    protected ContentMutationHandlerBase(IContentBlockRepository repository, IMarketingUnitOfWork unitOfWork)
    {
        Repository = repository;
        UnitOfWork = unitOfWork;
    }

    protected async Task<Result> MutateAsync(Guid id, Func<ContentBlock, Result> mutate, CancellationToken ct)
    {
        var block = await Repository.GetByIdAsync(new ContentBlockId(id), ct);
        if (block is null)
        {
            return Result.Failure(Error.NotFound("marketing.content.not_found", "Bloc de contenu introuvable."));
        }

        var result = mutate(block);
        if (result.IsFailure)
        {
            return result;
        }

        await UnitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class PublishContentBlockCommandHandler : ContentMutationHandlerBase, ICommandHandler<PublishContentBlockCommand>
{
    public PublishContentBlockCommandHandler(IContentBlockRepository r, IMarketingUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(PublishContentBlockCommand c, CancellationToken ct) => MutateAsync(c.ContentBlockId, b => b.Publish(), ct);
}

internal sealed class ArchiveContentBlockCommandHandler : ContentMutationHandlerBase, ICommandHandler<ArchiveContentBlockCommand>
{
    public ArchiveContentBlockCommandHandler(IContentBlockRepository r, IMarketingUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(ArchiveContentBlockCommand c, CancellationToken ct) => MutateAsync(c.ContentBlockId, b => b.Archive(), ct);
}

internal sealed class ListContentBySlotQueryHandler : IQueryHandler<ListContentBySlotQuery, IReadOnlyList<ContentBlockSummary>>
{
    private readonly IContentBlockRepository _repository;
    public ListContentBySlotQueryHandler(IContentBlockRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ContentBlockSummary>>> Handle(ListContentBySlotQuery query, CancellationToken cancellationToken)
    {
        var blocks = await _repository.ListPublishedBySlotAsync(query.Slot, cancellationToken);
        IReadOnlyList<ContentBlockSummary> summaries = blocks.Select(ContentMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
