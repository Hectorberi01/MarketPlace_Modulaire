using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Shipping.Application.Abstractions;
using Marketplace.Modules.Shipping.Domain.Shipments;

namespace Marketplace.Modules.Shipping.Application.Shipments.Commands;

/// <summary>Marque l'expédition « en préparation ».</summary>
public sealed record MarkShipmentPreparingCommand(Guid ShipmentId) : ICommand;

/// <summary>Marque l'expédition « expédiée » (transporteur + numéro de suivi).</summary>
public sealed record MarkShipmentShippedCommand(Guid ShipmentId, string Carrier, string TrackingNumber) : ICommand;

/// <summary>Marque l'expédition « livrée ».</summary>
public sealed record MarkShipmentDeliveredCommand(Guid ShipmentId) : ICommand;

/// <summary>Annule l'expédition.</summary>
public sealed record CancelShipmentCommand(Guid ShipmentId) : ICommand;

internal abstract class ShipmentCommandHandlerBase
{
    protected readonly IShipmentRepository Repository;
    protected readonly IShippingUnitOfWork UnitOfWork;

    protected ShipmentCommandHandlerBase(IShipmentRepository repository, IShippingUnitOfWork unitOfWork)
    {
        Repository = repository;
        UnitOfWork = unitOfWork;
    }

    protected async Task<Result> MutateAsync(Guid shipmentId, Func<Shipment, Result> mutate, CancellationToken ct)
    {
        var shipment = await Repository.GetByIdAsync(new ShipmentId(shipmentId), ct);
        if (shipment is null)
        {
            return Result.Failure(Error.NotFound("shipping.not_found", "Expédition introuvable."));
        }

        var result = mutate(shipment);
        if (result.IsFailure)
        {
            return result;
        }

        await UnitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class MarkShipmentPreparingCommandHandler : ShipmentCommandHandlerBase, ICommandHandler<MarkShipmentPreparingCommand>
{
    public MarkShipmentPreparingCommandHandler(IShipmentRepository repository, IShippingUnitOfWork unitOfWork)
        : base(repository, unitOfWork) { }

    public Task<Result> Handle(MarkShipmentPreparingCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.ShipmentId, s => s.MarkPreparing(), cancellationToken);
}

internal sealed class MarkShipmentShippedCommandHandler : ShipmentCommandHandlerBase, ICommandHandler<MarkShipmentShippedCommand>
{
    public MarkShipmentShippedCommandHandler(IShipmentRepository repository, IShippingUnitOfWork unitOfWork)
        : base(repository, unitOfWork) { }

    public Task<Result> Handle(MarkShipmentShippedCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.ShipmentId, s => s.MarkShipped(command.Carrier, command.TrackingNumber), cancellationToken);
}

internal sealed class MarkShipmentDeliveredCommandHandler : ShipmentCommandHandlerBase, ICommandHandler<MarkShipmentDeliveredCommand>
{
    public MarkShipmentDeliveredCommandHandler(IShipmentRepository repository, IShippingUnitOfWork unitOfWork)
        : base(repository, unitOfWork) { }

    public Task<Result> Handle(MarkShipmentDeliveredCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.ShipmentId, s => s.MarkDelivered(), cancellationToken);
}

internal sealed class CancelShipmentCommandHandler : ShipmentCommandHandlerBase, ICommandHandler<CancelShipmentCommand>
{
    public CancelShipmentCommandHandler(IShipmentRepository repository, IShippingUnitOfWork unitOfWork)
        : base(repository, unitOfWork) { }

    public Task<Result> Handle(CancelShipmentCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.ShipmentId, s => s.Cancel(), cancellationToken);
}
