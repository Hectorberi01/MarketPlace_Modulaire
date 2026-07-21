using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.RequestSellerClosure;

internal sealed class RequestSellerClosureCommandHandler : ICommandHandler<RequestSellerClosureCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public RequestSellerClosureCommandHandler(ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RequestSellerClosureCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        var result = seller.RequestClosure();
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
