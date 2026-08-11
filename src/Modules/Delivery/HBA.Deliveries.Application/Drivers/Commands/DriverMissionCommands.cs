using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Application.Drivers.Commands;

/// <summary>Le livreur accepte la mission qui lui est proposée.</summary>
public sealed record AcceptDeliveryCommand(Guid DeliveryId, Guid DriverId) : ICommand;

/// <summary>Le livreur refuse. Le motif est facultatif et sert au réglage du dispatch.</summary>
public sealed record RejectDeliveryCommand(Guid DeliveryId, Guid DriverId, string? Reason = null) : ICommand;

/// <summary>
/// Le délai de réponse est écoulé sans réaction du livreur. Émis par le service de
/// fond, jamais par l'application mobile.
/// </summary>
public sealed record ExpireDeliveryOfferCommand(Guid DeliveryId, Guid DriverId) : ICommand;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ACCEPTER UNE MISSION TOUCHE DEUX AGRÉGATS.
///
/// La course passe à « acceptée », le livreur passe à « en course ». Aucun des
/// deux n'a le droit de piloter l'autre — un agrégat qui en modifie un autre est
/// la porte d'entrée des incohérences. C'est donc la couche Application qui
/// orchestre, et l'Unit of Work qui garantit que les deux changements partent
/// ensemble ou pas du tout.
///
/// L'ORDRE COMPTE. On réserve d'abord LE LIVREUR, ensuite la course :
///
///   • si le livreur vient d'accepter une autre mission, <c>MarkBusy</c> échoue et
///     la course reste proposable à quelqu'un d'autre — coût nul ;
///   • dans l'ordre inverse, la course serait acceptée puis on découvrirait que le
///     livreur est déjà pris : il faudrait défaire, et une course « acceptée par
///     personne » est exactement l'état dont on ne sait pas sortir.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class AcceptDeliveryCommandHandler : ICommandHandler<AcceptDeliveryCommand>
{
    private readonly IDeliveryRepository _deliveries;
    private readonly IDriverRepository _drivers;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public AcceptDeliveryCommandHandler(
        IDeliveryRepository deliveries, IDriverRepository drivers, IDeliveryUnitOfWork unitOfWork)
    {
        _deliveries = deliveries;
        _drivers = drivers;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AcceptDeliveryCommand command, CancellationToken cancellationToken)
    {
        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(command.DeliveryId), cancellationToken);
        if (delivery is null)
        {
            return Result.Failure(Error.NotFound("delivery.not_found", "Course introuvable."));
        }

        var driverId = new DriverId(command.DriverId);
        var driver = await _drivers.GetByIdAsync(driverId, cancellationToken);
        if (driver is null)
        {
            return Result.Failure(Error.NotFound("driver.not_found", "Livreur introuvable."));
        }

        // Le livreur d'abord — voir l'encadré ci-dessus.
        var busy = driver.MarkBusy();
        if (busy.IsFailure)
        {
            return busy;
        }

        var accepted = delivery.AcceptByDriver(driverId);
        if (accepted.IsFailure)
        {
            return accepted;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Refus, ou expiration du délai. Les deux suivent le même chemin : la course
/// retourne en recherche, et un refus n'est PAS un incident — c'est le
/// fonctionnement normal du dispatch.
/// </summary>
internal sealed class RejectDeliveryCommandHandler
    : ICommandHandler<RejectDeliveryCommand>, ICommandHandler<ExpireDeliveryOfferCommand>
{
    private readonly IDeliveryRepository _deliveries;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public RejectDeliveryCommandHandler(IDeliveryRepository deliveries, IDeliveryUnitOfWork unitOfWork)
    {
        _deliveries = deliveries;
        _unitOfWork = unitOfWork;
    }

    public Task<Result> Handle(RejectDeliveryCommand command, CancellationToken cancellationToken)
        => RejectAsync(command.DeliveryId, command.DriverId, command.Reason, expired: false, cancellationToken);

    public Task<Result> Handle(ExpireDeliveryOfferCommand command, CancellationToken cancellationToken)
        => RejectAsync(command.DeliveryId, command.DriverId, reason: null, expired: true, cancellationToken);

    private async Task<Result> RejectAsync(
        Guid deliveryId, Guid driverId, string? reason, bool expired, CancellationToken cancellationToken)
    {
        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(deliveryId), cancellationToken);
        if (delivery is null)
        {
            return Result.Failure(Error.NotFound("delivery.not_found", "Course introuvable."));
        }

        // Le livreur n'est PAS repassé « disponible » ici : il ne l'a jamais quitté.
        // On ne marque « en course » qu'à l'acceptation, précisément pour que le
        // refus n'ait aucun effet de bord à défaire.
        var result = delivery.RejectByDriver(new DriverId(driverId), reason, expired);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
