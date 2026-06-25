using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.SetPayoutAccount;

internal sealed class SetPayoutAccountCommandHandler : ICommandHandler<SetPayoutAccountCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public SetPayoutAccountCommandHandler(ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetPayoutAccountCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        if (!Enum.TryParse<PayoutProvider>(command.Provider, ignoreCase: true, out var provider))
        {
            return Result.Failure(Error.Validation("sellers.payout.provider_invalid", "Canal de reversement invalide (MtnMomo, MoovMoney, Wave, BankAccount)."));
        }

        var payoutResult = PayoutAccount.Create(provider, command.AccountNumber, command.AccountName);
        if (payoutResult.IsFailure)
        {
            return Result.Failure(payoutResult.Error);
        }

        seller.SetPayoutAccount(payoutResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
