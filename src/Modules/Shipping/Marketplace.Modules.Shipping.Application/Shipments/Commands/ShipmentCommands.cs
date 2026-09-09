using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Shipping.Application.Abstractions;
using Marketplace.Modules.Shipping.Domain.Carriers;
using Marketplace.Modules.Shipping.Domain.Shipments;

namespace Marketplace.Modules.Shipping.Application.Shipments.Commands;

/// <summary>Marque l'expédition « en préparation ».</summary>
public sealed record MarkShipmentPreparingCommand(Guid ShipmentId) : ICommand;

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

/// <summary>
/// Le coursier a livré la commande : toutes ses expéditions encore en préparation
/// passent « livrées ».
///
/// ═════════════════════════════════════════════════════════════════════════════
/// PAR COMMANDE, PAS PAR EXPÉDITION — PARCE QUE LA COURSE EST PAR COMMANDE.
///
/// Le service de dispatch crée UNE course par commande, avec un point de ramassage par
/// part vendeur et un seul point de dépôt. Quand le coursier annonce la course livrée,
/// il a donc déposé toutes les parts d'un coup : il n'existe aucun moment où une seule
/// expédition de la commande serait livrée par lui. L'annonce ne porte d'ailleurs pas
/// d'identifiant d'expédition, seulement la commande et la course.
///
/// LES EXPÉDITIONS « EN TRANSIT » SONT IGNORÉES, PAS REFUSÉES. Une part que le repli en
/// cascade a confiée à un transporteur tiers n'appartient plus à ce coursier ; c'est le
/// transporteur qui la clôturera, par `MarkDelivered`. Faire échouer l'annonce à cause
/// d'elle empêcherait de clôturer les parts qu'il a réellement portées.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record MarkOrderDeliveredByCourierCommand(Guid OrderId, Guid DeliveryJobId) : ICommand;

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

internal sealed class MarkOrderDeliveredByCourierCommandHandler
    : ICommandHandler<MarkOrderDeliveredByCourierCommand>
{
    private readonly IShipmentRepository _repository;
    private readonly IShippingUnitOfWork _unitOfWork;

    public MarkOrderDeliveredByCourierCommandHandler(
        IShipmentRepository repository, IShippingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        MarkOrderDeliveredByCourierCommand command, CancellationToken cancellationToken)
    {
        var shipments = await _repository.ListByOrderAsync(command.OrderId, cancellationToken);

        // AUCUNE EXPÉDITION POUR UNE COMMANDE LIVRÉE : on échoue, et c'est délibéré.
        //
        // Le coursier annonce avoir déposé une commande dont le monolithe ne connaît
        // aucune part. C'est une incohérence entre les deux systèmes, pas un cas
        // limite : la taire laisserait la commande bloquée en « confirmée » sans que
        // personne ne sache pourquoi. L'échec remonte au service de dispatch, sa file de
        // sortie réessaie, puis met en lettre morte — et la lettre morte est journalisée
        // en Critical. C'est le seul chemin par lequel quelqu'un l'apprendra.
        if (shipments.Count == 0)
        {
            return Result.Failure(Error.NotFound(
                "shipping.order_has_no_shipment",
                $"Aucune expédition pour la commande {command.OrderId}."));
        }

        var closed = 0;
        var alreadyClosed = 0;

        foreach (var shipment in shipments)
        {
            // Confiée à un transporteur tiers : ce n'est plus la course de ce coursier.
            if (shipment.Status is ShipmentStatus.Shipped or ShipmentStatus.Cancelled)
            {
                continue;
            }

            if (shipment.Status == ShipmentStatus.Delivered)
            {
                alreadyClosed++;
                continue;
            }

            var result = shipment.MarkDeliveredByCourier(command.DeliveryJobId);
            if (result.IsFailure)
            {
                // On ne sauve RIEN de ce qui précède : une commande à demi clôturée
                // laisserait `MarkOrderDeliveredOnAllShipmentsDelivered` en suspens sans
                // trace, et le rejeu — que le dispatch fera — ne reprendrait pas le
                // reste puisque les premières parts seraient déjà livrées.
                return result;
            }

            closed++;
        }

        if (closed == 0)
        {
            // Rien à faire ET rien de déjà fait : toutes les parts sont chez un tiers ou
            // annulées. Le coursier n'aurait pas dû porter cette commande.
            return alreadyClosed > 0
                ? Result.Success()
                : Result.Failure(Error.Conflict(
                    "shipping.no_shipment_to_deliver",
                    $"Aucune expédition en préparation pour la commande {command.OrderId}."));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class CancelShipmentCommandHandler : ShipmentCommandHandlerBase, ICommandHandler<CancelShipmentCommand>
{
    public CancelShipmentCommandHandler(IShipmentRepository repository, IShippingUnitOfWork unitOfWork)
        : base(repository, unitOfWork) { }

    public Task<Result> Handle(CancelShipmentCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.ShipmentId, s => s.Cancel(), cancellationToken);
}
