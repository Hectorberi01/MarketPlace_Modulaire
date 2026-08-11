using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Shipping.Application.Abstractions;
using Marketplace.Modules.Shipping.Domain.Carriers;
using Marketplace.Modules.Shipping.Domain.Shipments;

namespace Marketplace.Modules.Shipping.Application.Shipments.Commands;

/// <summary>Marque l'expédition « en préparation ».</summary>
public sealed record MarkShipmentPreparingCommand(Guid ShipmentId) : ICommand;

/// <summary>
/// Le vendeur déclare le colis emballé et prêt à enlever. C'est ce geste qui
/// déclenche la recherche d'un livreur.
/// </summary>
public sealed record MarkShipmentReadyForPickupCommand(Guid ShipmentId) : ICommand;

/// <summary>
/// Marque l'expédition « expédiée » (transporteur + numéro de suivi). Si [CarrierId]
/// est fourni (transporteur du catalogue), il fait AUTORITÉ : son nom remplace le
/// texte libre et son modèle d'URL produit le lien de suivi cliquable.
/// </summary>
public sealed record MarkShipmentShippedCommand(Guid ShipmentId, string Carrier, string TrackingNumber, Guid? CarrierId = null) : ICommand;

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

internal sealed class MarkShipmentReadyForPickupCommandHandler
    : ShipmentCommandHandlerBase, ICommandHandler<MarkShipmentReadyForPickupCommand>
{
    public MarkShipmentReadyForPickupCommandHandler(IShipmentRepository repository, IShippingUnitOfWork unitOfWork)
        : base(repository, unitOfWork) { }

    public Task<Result> Handle(MarkShipmentReadyForPickupCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.ShipmentId, s => s.MarkReadyForPickup(), cancellationToken);
}

internal sealed class MarkShipmentShippedCommandHandler : ShipmentCommandHandlerBase, ICommandHandler<MarkShipmentShippedCommand>
{
    private readonly ICarrierRepository _carriers;

    public MarkShipmentShippedCommandHandler(IShipmentRepository repository, IShippingUnitOfWork unitOfWork, ICarrierRepository carriers)
        : base(repository, unitOfWork) => _carriers = carriers;

    public async Task<Result> Handle(MarkShipmentShippedCommand command, CancellationToken cancellationToken)
    {
        var carrierName = command.Carrier;
        string? trackingUrl = null;

        // Transporteur du catalogue : il fait autorité (nom + lien de suivi).
        if (command.CarrierId is { } carrierId)
        {
            var carrier = await _carriers.GetByIdAsync(new CarrierId(carrierId), cancellationToken);
            if (carrier is null)
            {
                return Result.Failure(Error.NotFound("shipping.carrier.not_found", "Transporteur introuvable."));
            }

            if (!carrier.IsActive)
            {
                return Result.Failure(Error.Conflict("shipping.carrier.inactive", "Ce transporteur n'est plus actif."));
            }

            carrierName = carrier.Name;
            trackingUrl = carrier.BuildTrackingUrl(command.TrackingNumber);
        }

        return await MutateAsync(
            command.ShipmentId,
            s => s.MarkShipped(carrierName, command.TrackingNumber, trackingUrl),
            cancellationToken);
    }
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
