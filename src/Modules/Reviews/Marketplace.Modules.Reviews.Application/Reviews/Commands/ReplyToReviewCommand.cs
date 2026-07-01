using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Reviews.Application.Abstractions;
using Marketplace.Modules.Reviews.Domain.Reviews;

namespace Marketplace.Modules.Reviews.Application.Reviews.Commands;

/// <summary>Réponse publique du vendeur à un avis.</summary>
public sealed record ReplyToReviewCommand(Guid ReviewId, string Body) : ICommand;

internal sealed class ReplyToReviewCommandHandler : ICommandHandler<ReplyToReviewCommand>
{
    private readonly IReviewRepository _repository;
    private readonly IReviewsUnitOfWork _unitOfWork;

    public ReplyToReviewCommandHandler(IReviewRepository repository, IReviewsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReplyToReviewCommand command, CancellationToken cancellationToken)
    {
        var review = await _repository.GetByIdAsync(new ReviewId(command.ReviewId), cancellationToken);
        if (review is null)
        {
            return Result.Failure(Error.NotFound("reviews.not_found", "Avis introuvable."));
        }

        var reply = review.Reply(command.Body);
        if (reply.IsFailure)
        {
            return reply;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
