using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Offers.Application.Abstractions;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.DeleteOffer;

internal sealed class DeleteOfferCommandHandler : ICommandHandler<DeleteOfferCommand>
{
    private readonly IOfferRepository _offerRepository;
    private readonly IOffersUnitOfWork _unitOfWork;

    public DeleteOfferCommandHandler(IOfferRepository offerRepository, IOffersUnitOfWork unitOfWork)
    {
        _offerRepository = offerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteOfferCommand command, CancellationToken cancellationToken)
    {
        var offer = await _offerRepository.GetByIdAsync(new OfferId(command.OfferId), cancellationToken);
        if (offer is null)
        {
            // Idempotent : supprimer deux fois n'est pas une erreur. Un vendeur qui
            // reclique parce que la première réponse a tardé ne doit pas voir un échec
            // là où le résultat voulu est déjà atteint.
            return Result.Success();
        }

        _offerRepository.Remove(offer);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
