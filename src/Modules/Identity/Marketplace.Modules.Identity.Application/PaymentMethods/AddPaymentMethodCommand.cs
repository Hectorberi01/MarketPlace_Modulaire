using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.PaymentMethods;

namespace Marketplace.Modules.Identity.Application.PaymentMethods;

/// <summary>
/// Enregistre un moyen de paiement. <see cref="Type"/> vaut « MobileMoney » ou
/// « Card ». Le numéro de carte n'est utilisé que pour en extraire les 4 derniers
/// chiffres (il n'est jamais persisté).
/// </summary>
public sealed record AddPaymentMethodCommand(
    Guid UserId,
    string Type,
    string? Label,
    string Provider,
    string? Msisdn,
    string? CardNumber,
    int? ExpiryMonth,
    int? ExpiryYear,
    string? HolderName,
    bool MakeDefault) : ICommand<Guid>;

internal sealed class AddPaymentMethodCommandHandler : ICommandHandler<AddPaymentMethodCommand, Guid>
{
    private readonly IPaymentMethodRepository _repository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public AddPaymentMethodCommandHandler(IPaymentMethodRepository repository, IIdentityUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(AddPaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var existing = await _repository.ListByUserAsync(command.UserId, cancellationToken);
        var makeDefault = command.MakeDefault || existing.Count == 0;

        Result<PaymentMethod> created = command.Type?.Trim().ToLowerInvariant() switch
        {
            "card" => PaymentMethod.CreateCard(
                command.UserId, command.Label, command.Provider, command.CardNumber ?? string.Empty,
                command.ExpiryMonth ?? 0, command.ExpiryYear ?? 0, command.HolderName, makeDefault),
            "mobilemoney" or "mobile_money" or "momo" => PaymentMethod.CreateMobileMoney(
                command.UserId, command.Label, command.Provider, command.Msisdn ?? string.Empty, makeDefault),
            _ => Result.Failure<PaymentMethod>(Error.Validation("identity.payment_method.type_invalid", "Type de moyen de paiement inconnu."))
        };

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        if (makeDefault)
        {
            foreach (var other in existing.Where(m => m.IsDefault))
            {
                other.ClearDefault();
            }
        }

        await _repository.AddAsync(created.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(created.Value.Id.Value);
    }
}
