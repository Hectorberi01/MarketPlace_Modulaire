using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.PaymentMethods;

namespace Marketplace.Modules.Identity.Application.PaymentMethods;

/// <summary>Définit le moyen de paiement par défaut de l'utilisateur.</summary>
public sealed record SetDefaultPaymentMethodCommand(Guid UserId, Guid PaymentMethodId) : ICommand;

internal sealed class SetDefaultPaymentMethodCommandHandler : ICommandHandler<SetDefaultPaymentMethodCommand>
{
    private readonly IPaymentMethodRepository _repository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public SetDefaultPaymentMethodCommandHandler(IPaymentMethodRepository repository, IIdentityUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetDefaultPaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var methods = await _repository.ListByUserAsync(command.UserId, cancellationToken);
        var target = methods.FirstOrDefault(m => m.Id.Value == command.PaymentMethodId);
        if (target is null)
        {
            return Result.Failure(Error.NotFound("identity.payment_method.not_found", "Moyen de paiement introuvable."));
        }

        foreach (var method in methods.Where(m => m.IsDefault))
        {
            method.ClearDefault();
        }

        target.MarkDefault();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
