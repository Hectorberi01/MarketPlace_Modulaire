using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.UpdateSellerProfile;

internal sealed class UpdateSellerProfileCommandHandler : ICommandHandler<UpdateSellerProfileCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public UpdateSellerProfileCommandHandler(ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateSellerProfileCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        var result = seller.UpdateProfile(command.ShopName, command.LogoUrl, command.Description);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
