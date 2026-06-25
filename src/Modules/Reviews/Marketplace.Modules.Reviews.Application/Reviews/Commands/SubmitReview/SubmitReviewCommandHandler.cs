using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Reviews.Application.Abstractions;
using Marketplace.Modules.Reviews.Domain.Reviews;

namespace Marketplace.Modules.Reviews.Application.Reviews.Commands.SubmitReview;

/// <summary>
/// Dépose un avis « achat vérifié » : vérifie via Ordering (Contracts) que la
/// commande appartient à l'acheteur, qu'elle est confirmée et qu'elle contient
/// le produit, refuse les doublons, puis publie l'avis.
/// </summary>
internal sealed class SubmitReviewCommandHandler : ICommandHandler<SubmitReviewCommand, Guid>
{
    private readonly IReviewRepository _reviewRepository;
    private readonly IOrderingModuleApi _orderingModuleApi;
    private readonly IReviewsUnitOfWork _unitOfWork;

    public SubmitReviewCommandHandler(
        IReviewRepository reviewRepository,
        IOrderingModuleApi orderingModuleApi,
        IReviewsUnitOfWork unitOfWork)
    {
        _reviewRepository = reviewRepository;
        _orderingModuleApi = orderingModuleApi;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(SubmitReviewCommand command, CancellationToken cancellationToken)
    {
        var order = await _orderingModuleApi.GetOrderAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<Guid>(Error.NotFound("reviews.order.not_found", "Commande introuvable."));
        }

        if (order.BuyerId != command.BuyerId)
        {
            return Result.Failure<Guid>(Error.Forbidden("reviews.not_owner", "Cette commande n'appartient pas à l'acheteur."));
        }

        if (!string.Equals(order.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<Guid>(Error.Conflict("reviews.order_not_confirmed", "Seule une commande confirmée peut être notée."));
        }

        var line = order.Lines.FirstOrDefault(l => l.ProductId == command.ProductId);
        if (line is null)
        {
            return Result.Failure<Guid>(Error.Conflict("reviews.product_not_in_order", "Ce produit n'est pas dans la commande."));
        }

        if (await _reviewRepository.ExistsAsync(command.BuyerId, command.ProductId, command.OrderId, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict("reviews.already_reviewed", "Vous avez déjà noté ce produit pour cette commande."));
        }

        var rating = Rating.Create(command.Rating);
        if (rating.IsFailure)
        {
            return Result.Failure<Guid>(rating.Error);
        }

        var reviewResult = Review.Create(
            command.ProductId, line.SellerId, command.BuyerId, command.OrderId,
            rating.Value, command.Title, command.Body, isVerifiedPurchase: true);

        if (reviewResult.IsFailure)
        {
            return Result.Failure<Guid>(reviewResult.Error);
        }

        await _reviewRepository.AddAsync(reviewResult.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return reviewResult.Value.Id.Value;
    }
}
