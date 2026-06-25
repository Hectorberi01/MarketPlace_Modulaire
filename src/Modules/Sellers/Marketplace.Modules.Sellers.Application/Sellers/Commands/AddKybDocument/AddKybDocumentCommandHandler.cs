using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.AddKybDocument;

internal sealed class AddKybDocumentCommandHandler : ICommandHandler<AddKybDocumentCommand, Guid>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public AddKybDocumentCommandHandler(ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(AddKybDocumentCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable.");
        }

        if (!Enum.TryParse<KybDocumentType>(command.Type, ignoreCase: true, out var type))
        {
            return Error.Validation("sellers.kyb.type_invalid", "Type de pièce KYB invalide.");
        }

        var result = seller.AddKybDocument(type, command.FileUrl);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id;
    }
}
