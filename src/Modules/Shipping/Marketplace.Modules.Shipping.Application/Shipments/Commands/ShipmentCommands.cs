using Microsoft.Extensions.Logging;
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

/// <summary>
/// Le coursier a DÉCLARÉ SA COURSE EN ÉCHEC : les expéditions de la commande restent en
/// préparation, et le vendeur est prévenu qu'il doit reprendre la main.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CHEMIN QUI N'EXISTAIT PAS, ET SON ABSENCE NE PRODUISAIT AUCUNE ERREUR.
///
/// Le service de dispatch annonce désormais l'échec sur
/// `POST /api/delivery/courses/failed`. Cette route n'existait pas : le drain recevait
/// des 404, réessayait dix fois, abandonnait — et la marketplace n'apprenait jamais
/// rien. L'expédition restait « en préparation » indéfiniment, la commande payée
/// n'atteignait aucun état final, et personne — ni le vendeur qui détient le colis, ni
/// l'exploitant — ne pouvait le savoir.
///
/// ON NE CRÉDITE RIEN, ET C'EST LA DIFFÉRENCE DE FOND AVEC
/// <see cref="MarkOrderDeliveredByCourierCommand"/>. Aucune livraison n'a eu lieu.
/// L'annonce ne porte d'ailleurs aucun montant, précisément pour qu'aucun code de ce
/// côté-ci ne puisse en déduire un paiement.
///
/// PAR COMMANDE, PAS PAR EXPÉDITION, pour la raison exacte qui vaut pour la livraison :
/// une course couvre une commande entière, avec un ramassage par part vendeur. Quand
/// elle échoue, TOUTES les parts qu'elle portait sont concernées d'un coup, et l'annonce
/// ne transporte aucun identifiant d'expédition.
///
/// [CourierId] N'EST PAS UTILISÉ POUR DÉCIDER, seulement pour journaliser. Il ne sert
/// donc à rien fonctionnellement — et c'est justement pour cela qu'il est là : sans lui,
/// une trace d'échec ne dit pas QUI a renoncé, et un coursier qui abandonne
/// systématiquement ses courses reste invisible.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record MarkOrderCourierDeliveryFailedCommand(
    Guid CourierId,
    Guid OrderId,
    Guid DeliveryJobId,
    string Reason,
    DateTime FailedAtUtc) : ICommand;

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

internal sealed class MarkOrderCourierDeliveryFailedCommandHandler
    : ICommandHandler<MarkOrderCourierDeliveryFailedCommand>
{
    private readonly IShipmentRepository _repository;
    private readonly IShippingUnitOfWork _unitOfWork;
    private readonly ILogger<MarkOrderCourierDeliveryFailedCommandHandler> _logger;

    public MarkOrderCourierDeliveryFailedCommandHandler(
        IShipmentRepository repository,
        IShippingUnitOfWork unitOfWork,
        ILogger<MarkOrderCourierDeliveryFailedCommandHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(
        MarkOrderCourierDeliveryFailedCommand command, CancellationToken cancellationToken)
    {
        var shipments = await _repository.ListByOrderAsync(command.OrderId, cancellationToken);

        // Même raisonnement que pour la livraison : une course annoncée sur une commande
        // dont le monolithe ne connaît aucune part est une incohérence entre les deux
        // systèmes. On échoue pour que le drain réessaie, puis mette en lettre morte —
        // et la lettre morte, elle, est journalisée en Critical par l'outbox.
        if (shipments.Count == 0)
        {
            return Result.Failure(Error.NotFound(
                "shipping.order_has_no_shipment",
                $"Aucune expédition pour la commande {command.OrderId}."));
        }

        var recorded = 0;
        var alreadyRecorded = 0;

        foreach (var shipment in shipments)
        {
            // ─────────────────────────────────────────────────────────────────────
            // LE REJEU SE DÉTECTE AVANT D'APPELER, PAS APRÈS.
            //
            // `MarkCourierDeliveryFailed` rend un succès silencieux sur un rejeu — donc
            // un succès ne dit PAS si quelque chose a changé. Compter les succès ferait
            // republier l'événement et reprévenir le vendeur à chaque tentative du
            // drain, ce qui est exactement le défaut que l'idempotence doit fermer.
            // On compare donc la course AVANT, comme `alreadyClosed` le fait pour la
            // livraison.
            // ─────────────────────────────────────────────────────────────────────
            if (shipment.FailedDeliveryJobId == command.DeliveryJobId)
            {
                alreadyRecorded++;
                continue;
            }

            // Confiée à un transporteur tiers, déjà livrée, ou annulée : cette part
            // n'appartient plus à la course qui vient d'échouer. On l'ignore au lieu de
            // faire échouer l'annonce — sinon une seule part partie chez un tiers
            // empêcherait de débloquer toutes les autres.
            if (shipment.Status is not ShipmentStatus.Preparing)
            {
                continue;
            }

            var result = shipment.MarkCourierDeliveryFailed(
                command.DeliveryJobId, command.Reason, command.FailedAtUtc);

            if (result.IsFailure)
            {
                // On ne sauve RIEN de ce qui précède. Une commande à demi annotée
                // laisserait certains vendeurs prévenus et d'autres non, et le rejeu —
                // que le dispatch fera — ne reprendrait pas les manquants, puisque les
                // premières parts porteraient déjà la course.
                return result;
            }

            recorded++;
        }

        if (recorded == 0)
        {
            // Rejeu intégral : succès silencieux, aucune notification de plus.
            if (alreadyRecorded > 0)
            {
                return Result.Success();
            }

            // Rien à annoter et rien de déjà annoté : toutes les parts sont parties
            // ailleurs ou sont closes. Le coursier n'aurait pas dû porter cette commande.
            return Result.Failure(Error.Conflict(
                "shipping.no_shipment_to_fail",
                $"Aucune expédition en préparation pour la commande {command.OrderId}."));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // ═════════════════════════════════════════════════════════════════════════
        // UNE COMMANDE PAYÉE SANS LIVREUR EST UNE ANOMALIE D'EXPLOITATION.
        //
        // En Error, pas en Warning : l'argent de l'acheteur est encaissé, le colis est
        // immobilisé chez le vendeur, et le déblocage dépend maintenant d'une action
        // HUMAINE — le vendeur qui reprend par transporteur. Le service coursier, lui,
        // journalise l'échec en Warning, et c'est juste de son point de vue : une course
        // qu'on n'a pas pu finir fait partie de son métier. Elle ne fait pas partie de
        // celui de la marketplace, où elle laisse une vente payée en suspens. Reprendre
        // le Warning ici ferait passer l'incident sous le seuil d'alerte de la plupart
        // des collecteurs, et l'exploitant l'apprendrait par le client.
        //
        // APRÈS LE SAVE, ET APRÈS LA GARDE DE REJEU. Journaliser plus haut ferait dix
        // lignes d'erreur pour un seul échec — dix incidents apparents là où il n'y en a
        // qu'un, ce qui est la meilleure façon de faire ignorer le journal.
        //
        // Le motif est journalisé tel que la course le porte : c'est la seule chose qui
        // dit s'il faut réexpédier ou appeler l'acheteur.
        // ═════════════════════════════════════════════════════════════════════════
        _logger.LogError(
            "COURSE EN ÉCHEC — commande {OrderId}, course {DeliveryJobId}, coursier {CourierId} : "
            + "{Reason}. {Count} expédition(s) restent EN PRÉPARATION chez leur vendeur ; "
            + "la commande est payée et aucun colis n'est parti.",
            command.OrderId, command.DeliveryJobId, command.CourierId, command.Reason, recorded);

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
