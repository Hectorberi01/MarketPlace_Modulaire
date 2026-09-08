using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Delivery.Application.Abstractions;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Delivery.Domain.Couriers;

namespace Marketplace.Modules.Delivery.Application.Couriers;

/// <summary>Enregistre un coursier. Créé en attente de vérification : il n'est pas encore payable.</summary>
public sealed record RegisterCourierCommand(
    Guid UserId,
    string DisplayName,
    string Phone,
    string PayoutProvider,
    string PayoutAccountNumber,
    string? PayoutAccountName = null,
    string? ExternalRef = null) : ICommand<CourierSummary>;

public sealed class RegisterCourierCommandValidator : AbstractValidator<RegisterCourierCommand>
{
    public RegisterCourierCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Phone).NotEmpty();
        RuleFor(c => c.PayoutProvider).NotEmpty();
        RuleFor(c => c.PayoutAccountNumber).NotEmpty();
    }
}

/// <summary>Change le compte Mobile Money sur lequel le coursier est payé.</summary>
public sealed record ChangeCourierPayoutCommand(
    Guid CourierId, string Provider, string AccountNumber, string? AccountName = null) : ICommand<CourierSummary>;

/// <summary>Vérification terminée : le coursier devient payable.</summary>
public sealed record ActivateCourierCommand(Guid CourierId) : ICommand<CourierSummary>;

/// <summary>Suspend un coursier. Son solde reste le sien ; seuls les nouveaux crédits sont refusés.</summary>
public sealed record SuspendCourierCommand(Guid CourierId, string Reason) : ICommand<CourierSummary>;

/// <summary>Rattache un coursier à son identifiant dans le service de dispatch.</summary>
public sealed record LinkCourierExternalRefCommand(Guid CourierId, string ExternalRef) : ICommand<CourierSummary>;

internal sealed class RegisterCourierCommandHandler : ICommandHandler<RegisterCourierCommand, CourierSummary>
{
    private readonly ICourierRepository _couriers;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public RegisterCourierCommandHandler(ICourierRepository couriers, IDeliveryUnitOfWork unitOfWork)
    {
        _couriers = couriers;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CourierSummary>> Handle(RegisterCourierCommand command, CancellationToken cancellationToken)
    {
        // UN SEUL COMPTE COURSIER PAR UTILISATEUR. Sans ce garde, la même personne
        // pourrait porter deux portefeuilles livreur — et les courses payées à l'un
        // resteraient invisibles depuis l'autre.
        if (await _couriers.ExistsForUserAsync(command.UserId, cancellationToken))
        {
            return Result.Failure<CourierSummary>(Error.Conflict(
                "delivery.courier.already_registered", "Ce compte est déjà enregistré comme coursier."));
        }

        var courier = Courier.Register(
            command.UserId, command.DisplayName, command.Phone,
            command.PayoutProvider, command.PayoutAccountNumber, command.PayoutAccountName, command.ExternalRef);

        if (courier.IsFailure)
        {
            return Result.Failure<CourierSummary>(courier.Error);
        }

        await _couriers.AddAsync(courier.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return CourierMapper.ToSummary(courier.Value);
    }
}

/// <summary>Charge / mute / persiste — la forme partagée par toutes les mutations coursier.</summary>
internal abstract class CourierMutationHandlerBase
{
    protected readonly ICourierRepository Couriers;
    protected readonly IDeliveryUnitOfWork UnitOfWork;

    protected CourierMutationHandlerBase(ICourierRepository couriers, IDeliveryUnitOfWork unitOfWork)
    {
        Couriers = couriers;
        UnitOfWork = unitOfWork;
    }

    protected async Task<Result<CourierSummary>> MutateAsync(
        Guid courierId, Func<Courier, Result> mutate, CancellationToken cancellationToken)
    {
        var courier = await Couriers.GetByIdAsync(new CourierId(courierId), cancellationToken);
        if (courier is null)
        {
            return Result.Failure<CourierSummary>(Error.NotFound("delivery.courier.not_found", "Coursier introuvable."));
        }

        var mutated = mutate(courier);
        if (mutated.IsFailure)
        {
            return Result.Failure<CourierSummary>(mutated.Error);
        }

        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return CourierMapper.ToSummary(courier);
    }
}

internal sealed class ChangeCourierPayoutCommandHandler
    : CourierMutationHandlerBase, ICommandHandler<ChangeCourierPayoutCommand, CourierSummary>
{
    public ChangeCourierPayoutCommandHandler(ICourierRepository c, IDeliveryUnitOfWork u) : base(c, u) { }

    public Task<Result<CourierSummary>> Handle(ChangeCourierPayoutCommand command, CancellationToken ct)
        => MutateAsync(
            command.CourierId,
            courier => courier.ChangePayoutAccount(command.Provider, command.AccountNumber, command.AccountName),
            ct);
}

internal sealed class ActivateCourierCommandHandler
    : CourierMutationHandlerBase, ICommandHandler<ActivateCourierCommand, CourierSummary>
{
    public ActivateCourierCommandHandler(ICourierRepository c, IDeliveryUnitOfWork u) : base(c, u) { }

    public Task<Result<CourierSummary>> Handle(ActivateCourierCommand command, CancellationToken ct)
        => MutateAsync(command.CourierId, courier => courier.Activate(), ct);
}

internal sealed class SuspendCourierCommandHandler
    : CourierMutationHandlerBase, ICommandHandler<SuspendCourierCommand, CourierSummary>
{
    public SuspendCourierCommandHandler(ICourierRepository c, IDeliveryUnitOfWork u) : base(c, u) { }

    public Task<Result<CourierSummary>> Handle(SuspendCourierCommand command, CancellationToken ct)
        => MutateAsync(command.CourierId, courier => courier.Suspend(command.Reason), ct);
}

internal sealed class LinkCourierExternalRefCommandHandler
    : CourierMutationHandlerBase, ICommandHandler<LinkCourierExternalRefCommand, CourierSummary>
{
    public LinkCourierExternalRefCommandHandler(ICourierRepository c, IDeliveryUnitOfWork u) : base(c, u) { }

    public Task<Result<CourierSummary>> Handle(LinkCourierExternalRefCommand command, CancellationToken ct)
        => MutateAsync(
            command.CourierId,
            courier =>
            {
                courier.LinkExternalRef(command.ExternalRef);
                return Result.Success();
            },
            ct);
}
