using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Offers.Application.Abstractions;
using Marketplace.Modules.Offers.Contracts.IntegrationEvents;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.DeleteOffer;

internal sealed class DeleteOfferCommandHandler : ICommandHandler<DeleteOfferCommand>
{
    private readonly IOfferRepository _offerRepository;
    private readonly IOffersUnitOfWork _unitOfWork;
    private readonly IIntegrationEventPublisher _publisher;

    public DeleteOfferCommandHandler(
        IOfferRepository offerRepository,
        IOffersUnitOfWork unitOfWork,
        IIntegrationEventPublisher publisher)
    {
        _offerRepository = offerRepository;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<Result> Handle(DeleteOfferCommand command, CancellationToken cancellationToken)
    {
        var offer = await _offerRepository.GetByIdAsync(new OfferId(command.OfferId), cancellationToken);
        if (offer is null)
        {
            return Result.Failure(Error.NotFound("offers.offer.not_found", $"Offre {command.OfferId} introuvable."));
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // LE PRIX RESTANT SE CALCULE AVANT LA SUPPRESSION.
        //
        // `ListByProductAsync` interroge la base : l'entité marquée « Deleted » n'est
        // retirée qu'au `SaveChanges`, et la requête la renverrait encore. On lit donc
        // d'abord, puis on écarte explicitement celle qu'on s'apprête à supprimer.
        //
        // Sans ce calcul, l'index de recherche gardait le prix de l'offre disparue —
        // affiché sur les cartes ET utilisé pour le tri par prix. Un article retiré de
        // la vente continuait ainsi de peser sur le classement avec un montant fantôme.
        // ─────────────────────────────────────────────────────────────────────────────
        var siblings = await _offerRepository.ListByProductAsync(offer.ProductId, cancellationToken);
        var remaining = siblings.Where(o => o.Id != offer.Id).ToList();
        var cheapest = remaining.Count == 0
            ? null
            : remaining.MinBy(o => o.BasePrice.Amount);

        _offerRepository.Remove(offer);

        await _publisher.PublishAsync(
            new OfferDeletedIntegrationEvent
            {
                OfferId = offer.Id.Value,
                ProductId = offer.ProductId,
                RemainingMinPriceAmount = cheapest?.BasePrice.Amount,
                Currency = cheapest?.BasePrice.Currency,
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
