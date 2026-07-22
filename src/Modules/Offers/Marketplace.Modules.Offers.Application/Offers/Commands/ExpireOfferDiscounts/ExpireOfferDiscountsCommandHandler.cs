using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Offers.Application.Abstractions;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.ExpireOfferDiscounts;

internal sealed class ExpireOfferDiscountsCommandHandler : ICommandHandler<ExpireOfferDiscountsCommand, int>
{
    private readonly IOfferRepository _offerRepository;
    private readonly IOffersUnitOfWork _unitOfWork;
    private readonly PricingOptions _pricingOptions;

    public ExpireOfferDiscountsCommandHandler(
        IOfferRepository offerRepository, IOffersUnitOfWork unitOfWork, PricingOptions pricingOptions)
    {
        _offerRepository = offerRepository;
        _unitOfWork = unitOfWork;
        _pricingOptions = pricingOptions;
    }

    public async Task<Result<int>> Handle(ExpireOfferDiscountsCommand command, CancellationToken cancellationToken)
    {
        var offers = await _offerRepository.ListExpiredDiscountsAsync(DateTime.UtcNow, command.BatchSize, cancellationToken);

        var count = 0;
        foreach (var offer in offers)
        {
            var result = offer.RemoveDiscount(_pricingOptions.PlatformCommissionRate, _pricingOptions.ProviderFeeRate);
            if (result.IsSuccess)
            {
                count++;
            }
        }

        if (count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return count;
    }
}
