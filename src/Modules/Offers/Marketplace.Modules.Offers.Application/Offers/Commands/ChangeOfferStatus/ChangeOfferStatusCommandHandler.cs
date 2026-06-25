using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Offers.Application.Abstractions;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferStatus;

internal sealed class ChangeOfferStatusCommandHandler : ICommandHandler<ChangeOfferStatusCommand>
{
    private readonly IOfferRepository _offerRepository;
    private readonly IOffersUnitOfWork _unitOfWork;

    public ChangeOfferStatusCommandHandler(IOfferRepository offerRepository, IOffersUnitOfWork unitOfWork)
    {
        _offerRepository = offerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ChangeOfferStatusCommand command, CancellationToken cancellationToken)
    {
        var offer = await _offerRepository.GetByIdAsync(new OfferId(command.OfferId), cancellationToken);
        if (offer is null)
        {
            return Result.Failure(Error.NotFound("offers.offer.not_found", $"Offre {command.OfferId} introuvable."));
        }

        if (!Enum.TryParse<OfferStatus>(command.Status, ignoreCase: true, out var target))
        {
            return Result.Failure(Error.Validation("offers.offer.status_invalid", "Statut invalide (Active, Paused, OutOfStock)."));
        }

        var transition = target switch
        {
            OfferStatus.Active => offer.Activate(),
            OfferStatus.Paused => offer.Pause(),
            OfferStatus.OutOfStock => offer.MarkOutOfStock(),
            _ => Result.Failure(Error.Validation("offers.offer.status_invalid", "Statut invalide."))
        };

        if (transition.IsFailure)
        {
            return transition;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
