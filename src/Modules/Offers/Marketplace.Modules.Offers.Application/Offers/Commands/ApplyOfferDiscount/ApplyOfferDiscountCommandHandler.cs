using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Offers.Application.Abstractions;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.ApplyOfferDiscount;

internal sealed class ApplyOfferDiscountCommandHandler : ICommandHandler<ApplyOfferDiscountCommand>
{
    private readonly IOfferRepository _offerRepository;
    private readonly IOffersUnitOfWork _unitOfWork;
    private readonly PricingOptions _pricingOptions;

    public ApplyOfferDiscountCommandHandler(
        IOfferRepository offerRepository, IOffersUnitOfWork unitOfWork, PricingOptions pricingOptions)
    {
        _offerRepository = offerRepository;
        _unitOfWork = unitOfWork;
        _pricingOptions = pricingOptions;
    }

    public async Task<Result> Handle(ApplyOfferDiscountCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DiscountType>(command.DiscountType, ignoreCase: true, out var type))
        {
            return Result.Failure(Error.Validation("offers.discount.type_invalid", "Type de remise invalide (Percentage, Amount)."));
        }

        var offer = await _offerRepository.GetByIdAsync(new OfferId(command.OfferId), cancellationToken);
        if (offer is null)
        {
            return Result.Failure(Error.NotFound("offers.offer.not_found", $"Offre {command.OfferId} introuvable."));
        }

        var result = offer.ApplyDiscount(
            type, command.Value, command.EndsOnUtc,
            _pricingOptions.PlatformCommissionRate, _pricingOptions.ProviderFeeRate);

        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
