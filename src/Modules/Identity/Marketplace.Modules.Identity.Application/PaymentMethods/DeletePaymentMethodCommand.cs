using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.PaymentMethods;

namespace Marketplace.Modules.Identity.Application.PaymentMethods;

/// <summary>Supprime un moyen de paiement de l'utilisateur.</summary>
public sealed record DeletePaymentMethodCommand(Guid UserId, Guid PaymentMethodId) : ICommand;

internal sealed class DeletePaymentMethodCommandHandler : ICommandHandler<DeletePaymentMethodCommand>
{
    private readonly IPaymentMethodRepository _repository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public DeletePaymentMethodCommandHandler(IPaymentMethodRepository repository, IIdentityUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeletePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var method = await _repository.GetByIdAsync(new PaymentMethodId(command.PaymentMethodId), cancellationToken);
        if (method is null || method.UserId != command.UserId)
        {
            return Result.Failure(Error.NotFound("identity.payment_method.not_found", "Moyen de paiement introuvable."));
        }

        _repository.Remove(method);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
