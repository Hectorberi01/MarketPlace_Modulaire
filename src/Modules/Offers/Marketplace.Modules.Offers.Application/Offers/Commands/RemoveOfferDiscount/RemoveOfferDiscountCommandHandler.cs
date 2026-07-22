using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Offers.Application.Abstractions;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.RemoveOfferDiscount;

internal sealed class RemoveOfferDiscountCommandHandler : ICommandHandler<RemoveOfferDiscountCommand>
{
    private readonly IOfferRepository _offerRepository;
    private readonly IOffersUnitOfWork _unitOfWork;
    private readonly PricingOptions _pricingOptions;

    public RemoveOfferDiscountCommandHandler(
        IOfferRepository offerRepository, IOffersUnitOfWork unitOfWork, PricingOptions pricingOptions)
    {
        _offerRepository = offerRepository;
        _unitOfWork = unitOfWork;
        _pricingOptions = pricingOptions;
    }

    public async Task<Result> Handle(RemoveOfferDiscountCommand command, CancellationToken cancellationToken)
    {
        var offer = await _offerRepository.GetByIdAsync(new OfferId(command.OfferId), cancellationToken);
        if (offer is null)
        {
            return Result.Failure(Error.NotFound("offers.offer.not_found", $"Offre {command.OfferId} introuvable."));
        }

        var result = offer.RemoveDiscount(_pricingOptions.PlatformCommissionRate, _pricingOptions.ProviderFeeRate);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
