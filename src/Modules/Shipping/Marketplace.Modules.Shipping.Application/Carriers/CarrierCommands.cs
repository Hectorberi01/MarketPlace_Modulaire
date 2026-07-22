using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Shipping.Application.Abstractions;
using Marketplace.Modules.Shipping.Domain.Carriers;

namespace Marketplace.Modules.Shipping.Application.Carriers;

/// <summary>Crée un transporteur (admin). Renvoie son id.</summary>
public sealed record CreateCarrierCommand(string Name, string Code, string? TrackingUrlTemplate, string? LogoUrl) : ICommand<Guid>;

/// <summary>Met à jour un transporteur (le code est immuable).</summary>
public sealed record UpdateCarrierCommand(Guid CarrierId, string Name, string? TrackingUrlTemplate, string? LogoUrl) : ICommand;

/// <summary>Active / désactive un transporteur (un inactif n'est plus proposé au vendeur).</summary>
public sealed record SetCarrierActiveCommand(Guid CarrierId, bool Active) : ICommand;

internal sealed class CreateCarrierCommandHandler : ICommandHandler<CreateCarrierCommand, Guid>
{
    private readonly ICarrierRepository _repository;
    private readonly IShippingUnitOfWork _unitOfWork;

    public CreateCarrierCommandHandler(ICarrierRepository repository, IShippingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateCarrierCommand command, CancellationToken cancellationToken)
    {
        var result = Carrier.Create(command.Name, command.Code, command.TrackingUrlTemplate, command.LogoUrl);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        var carrier = result.Value;

        if (await _repository.CodeExistsAsync(carrier.Code, cancellationToken))
        {
            return Error.Conflict("shipping.carrier.code_taken", $"Un transporteur avec le code « {carrier.Code} » existe déjà.");
        }

        await _repository.AddAsync(carrier, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return carrier.Id.Value;
    }
}

internal sealed class UpdateCarrierCommandHandler : ICommandHandler<UpdateCarrierCommand>
{
    private readonly ICarrierRepository _repository;
    private readonly IShippingUnitOfWork _unitOfWork;

    public UpdateCarrierCommandHandler(ICarrierRepository repository, IShippingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateCarrierCommand command, CancellationToken cancellationToken)
    {
        var carrier = await _repository.GetByIdAsync(new CarrierId(command.CarrierId), cancellationToken);
        if (carrier is null)
        {
            return Result.Failure(Error.NotFound("shipping.carrier.not_found", "Transporteur introuvable."));
        }

        var result = carrier.Update(command.Name, command.TrackingUrlTemplate, command.LogoUrl);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SetCarrierActiveCommandHandler : ICommandHandler<SetCarrierActiveCommand>
{
    private readonly ICarrierRepository _repository;
    private readonly IShippingUnitOfWork _unitOfWork;

    public SetCarrierActiveCommandHandler(ICarrierRepository repository, IShippingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetCarrierActiveCommand command, CancellationToken cancellationToken)
    {
        var carrier = await _repository.GetByIdAsync(new CarrierId(command.CarrierId), cancellationToken);
        if (carrier is null)
        {
            return Result.Failure(Error.NotFound("shipping.carrier.not_found", "Transporteur introuvable."));
        }

        carrier.SetActive(command.Active);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class CreateCarrierCommandValidator : AbstractValidator<CreateCarrierCommand>
{
    public CreateCarrierCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Code).NotEmpty().MaximumLength(20);
    }
}

internal sealed class UpdateCarrierCommandValidator : AbstractValidator<UpdateCarrierCommand>
{
    public UpdateCarrierCommandValidator()
    {
        RuleFor(c => c.CarrierId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
    }
}
