using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Offers.Application.Abstractions;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferPrice;

internal sealed class ChangeOfferPriceCommandHandler : ICommandHandler<ChangeOfferPriceCommand>
{
    private readonly IOfferRepository _offerRepository;
    private readonly IOffersUnitOfWork _unitOfWork;

    public ChangeOfferPriceCommandHandler(IOfferRepository offerRepository, IOffersUnitOfWork unitOfWork)
    {
        _offerRepository = offerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ChangeOfferPriceCommand command, CancellationToken cancellationToken)
    {
        var offer = await _offerRepository.GetByIdAsync(new OfferId(command.OfferId), cancellationToken);
        if (offer is null)
        {
            return Result.Failure(Error.NotFound("offers.offer.not_found", $"Offre {command.OfferId} introuvable."));
        }

        var priceResult = Money.Create(command.Amount, command.Currency);
        if (priceResult.IsFailure)
        {
            return Result.Failure(priceResult.Error);
        }

        offer.ChangePrice(priceResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
