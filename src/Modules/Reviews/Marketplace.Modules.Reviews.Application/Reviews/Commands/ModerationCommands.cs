using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Reviews.Application.Abstractions;
using Marketplace.Modules.Reviews.Domain.Reviews;

namespace Marketplace.Modules.Reviews.Application.Reviews.Commands;

/// <summary>Signale un avis (modération).</summary>
public sealed record FlagReviewCommand(Guid ReviewId) : ICommand;

/// <summary>Rejette un avis (le retire de la note publique).</summary>
public sealed record RejectReviewCommand(Guid ReviewId) : ICommand;

/// <summary>Restaure un avis signalé / rejeté.</summary>
public sealed record RestoreReviewCommand(Guid ReviewId) : ICommand;

internal abstract class ReviewModerationHandlerBase
{
    protected readonly IReviewRepository Repository;
    protected readonly IReviewsUnitOfWork UnitOfWork;

    protected ReviewModerationHandlerBase(IReviewRepository repository, IReviewsUnitOfWork unitOfWork)
    {
        Repository = repository;
        UnitOfWork = unitOfWork;
    }

    protected async Task<Result> MutateAsync(Guid reviewId, Func<Review, Result> mutate, CancellationToken ct)
    {
        var review = await Repository.GetByIdAsync(new ReviewId(reviewId), ct);
        if (review is null)
        {
            return Result.Failure(Error.NotFound("reviews.not_found", "Avis introuvable."));
        }

        var result = mutate(review);
        if (result.IsFailure)
        {
            return result;
        }

        await UnitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class FlagReviewCommandHandler : ReviewModerationHandlerBase, ICommandHandler<FlagReviewCommand>
{
    public FlagReviewCommandHandler(IReviewRepository repository, IReviewsUnitOfWork unitOfWork) : base(repository, unitOfWork) { }

    public Task<Result> Handle(FlagReviewCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.ReviewId, r => r.Flag(), cancellationToken);
}

internal sealed class RejectReviewCommandHandler : ReviewModerationHandlerBase, ICommandHandler<RejectReviewCommand>
{
    public RejectReviewCommandHandler(IReviewRepository repository, IReviewsUnitOfWork unitOfWork) : base(repository, unitOfWork) { }

    public Task<Result> Handle(RejectReviewCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.ReviewId, r => r.Reject(), cancellationToken);
}

internal sealed class RestoreReviewCommandHandler : ReviewModerationHandlerBase, ICommandHandler<RestoreReviewCommand>
{
    public RestoreReviewCommandHandler(IReviewRepository repository, IReviewsUnitOfWork unitOfWork) : base(repository, unitOfWork) { }

    public Task<Result> Handle(RestoreReviewCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.ReviewId, r => r.Restore(), cancellationToken);
}
