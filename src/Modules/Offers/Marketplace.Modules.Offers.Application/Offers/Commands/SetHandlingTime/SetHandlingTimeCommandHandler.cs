using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Offers.Application.Abstractions;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.SetHandlingTime;

internal sealed class SetHandlingTimeCommandHandler : ICommandHandler<SetHandlingTimeCommand>
{
    private readonly IOfferRepository _offerRepository;
    private readonly IOffersUnitOfWork _unitOfWork;

    public SetHandlingTimeCommandHandler(IOfferRepository offerRepository, IOffersUnitOfWork unitOfWork)
    {
        _offerRepository = offerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetHandlingTimeCommand command, CancellationToken cancellationToken)
    {
        var offer = await _offerRepository.GetByIdAsync(new OfferId(command.OfferId), cancellationToken);
        if (offer is null)
        {
            return Result.Failure(Error.NotFound("offers.offer.not_found", $"Offre {command.OfferId} introuvable."));
        }

        var result = offer.SetHandlingTime(command.HandlingTime);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
