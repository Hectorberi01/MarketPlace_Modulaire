using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.PaymentMethods;

namespace Marketplace.Modules.Identity.Application.PaymentMethods;

/// <summary>
/// Met à jour un moyen de paiement enregistré (vérifie la propriété via
/// <see cref="UserId"/>). Pour Mobile Money : libellé, opérateur et numéro. Pour
/// Carte : libellé, expiration et titulaire (le numéro n'est jamais modifiable).
/// Si <see cref="MakeDefault"/> est vrai, le moyen devient le nouveau défaut.
/// </summary>
public sealed record UpdatePaymentMethodCommand(
    Guid UserId,
    Guid PaymentMethodId,
    string? Label,
    string? Provider,
    string? Msisdn,
    int? ExpiryMonth,
    int? ExpiryYear,
    string? HolderName,
    bool MakeDefault) : ICommand;

internal sealed class UpdatePaymentMethodCommandHandler : ICommandHandler<UpdatePaymentMethodCommand>
{
    private readonly IPaymentMethodRepository _repository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public UpdatePaymentMethodCommandHandler(IPaymentMethodRepository repository, IIdentityUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdatePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var methods = await _repository.ListByUserAsync(command.UserId, cancellationToken);
        var target = methods.FirstOrDefault(m => m.Id.Value == command.PaymentMethodId);
        if (target is null)
        {
            return Result.Failure(Error.NotFound("identity.payment_method.not_found", "Moyen de paiement introuvable."));
        }

        var updated = target.Type switch
        {
            PaymentMethodType.MobileMoney => target.UpdateMobileMoney(
                command.Label,
                string.IsNullOrWhiteSpace(command.Provider) ? target.Provider : command.Provider!,
                command.Msisdn ?? string.Empty),
            PaymentMethodType.Card => target.UpdateCard(
                command.Label,
                command.ExpiryMonth ?? target.ExpiryMonth ?? 0,
                command.ExpiryYear ?? target.ExpiryYear ?? 0,
                command.HolderName),
            _ => Result.Failure(Error.Validation("identity.payment_method.type_invalid", "Type de moyen de paiement inconnu.")),
        };

        if (updated.IsFailure)
        {
            return updated;
        }

        if (command.MakeDefault && !target.IsDefault)
        {
            foreach (var other in methods.Where(m => m.IsDefault))
            {
                other.ClearDefault();
            }

            target.MarkDefault();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
