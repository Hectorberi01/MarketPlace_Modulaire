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

        // MÊME RÉPONSE QUE « COMMANDE INTROUVABLE », À LA VIRGULE PRÈS.
        //
        // Ce site NE FIGURAIT PAS dans l'audit des routes — il a été trouvé en balayant
        // les `Error.Forbidden` restants après avoir corrigé les quatre signalés. Il a
        // exactement la forme des deux autres (`RequestReturnCommand`,
        // `OpenDisputeCommand`) : même lecture par `IOrderingModuleApi`, même 404 suivi
        // d'un 403, donc le même oracle sur l'existence d'une commande.
        //
        // Ce qui vaut d'être retenu : l'audit listait des ROUTES, et ce handler est
        // atteint par une route qu'il n'avait pas relevée. Chercher par code d'erreur
        // plutôt que par route aurait trouvé les cinq du premier coup.
        if (order.BuyerId != command.BuyerId)
        {
            return Result.Failure<Guid>(Error.NotFound("reviews.order.not_found", "Commande introuvable."));
        }

        // Une commande PAYÉE peut être notée : « Confirmed » (payée, en cours) ou
        // « Delivered » (livrée). L'app n'affiche « Noter » qu'après livraison —
        // exiger « Confirmed » SEUL rejetait donc toute commande livrée, rendant
        // l'avis impossible en pratique.
        var reviewable = string.Equals(order.Status, "Confirmed", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(order.Status, "Delivered", StringComparison.OrdinalIgnoreCase);
        if (!reviewable)
        {
            return Result.Failure<Guid>(Error.Conflict("reviews.order_not_confirmed", "Seule une commande payée peut être notée."));
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
