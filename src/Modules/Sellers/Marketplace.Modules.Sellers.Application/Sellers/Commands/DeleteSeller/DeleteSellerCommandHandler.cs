using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.DeleteSeller;

internal sealed class DeleteSellerCommandHandler : ICommandHandler<DeleteSellerCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public DeleteSellerCommandHandler(ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteSellerCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        // Émet l'événement de purge AVANT le retrait : le dispatch des domain events
        // lit le ChangeTracker avant le SaveChanges, l'agrégat (encore tracké, à
        // l'état Deleted) porte donc bien son événement.
        seller.MarkForDeletion();
        _sellerRepository.Remove(seller);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
