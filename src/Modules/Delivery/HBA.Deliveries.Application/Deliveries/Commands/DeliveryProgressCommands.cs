using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Application.Deliveries.Commands;

// ═════════════════════════════════════════════════════════════════════════════
// LES ÉTAPES D'EXÉCUTION PORTENT DÉSORMAIS « RequiredDriverId ».
//
// Il n'est renseigné que par les routes de l'application livreur, où il vient du
// JETON — jamais du corps de requête. Nul depuis la console d'exploitation, où
// l'appelant est un administrateur qui débloque une course à la main.
//
// Sans lui, tout livreur authentifié pouvait faire avancer la course d'un autre :
// la déclarer collectée, la déclarer livrée, et — depuis l'introduction du partage
// de recette — en encaisser le gain. Ce n'était pas une faille théorique : les
// identifiants de course circulent dans les journaux et les captures d'écran.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Le livreur est arrivé au point de collecte.</summary>
public sealed record MarkArrivedAtPickupCommand(Guid DeliveryId, Guid? RequiredDriverId = null) : ICommand;

/// <summary>Le colis est pris en charge.</summary>
public sealed record MarkPickedUpCommand(Guid DeliveryId, Guid? RequiredDriverId = null) : ICommand;

/// <summary>En route vers le destinataire.</summary>
public sealed record MarkInTransitCommand(Guid DeliveryId, Guid? RequiredDriverId = null) : ICommand;

/// <summary>Le livreur est arrivé chez le destinataire.</summary>
public sealed record MarkArrivedAtDropoffCommand(Guid DeliveryId, Guid? RequiredDriverId = null) : ICommand;

/// <summary>
/// Remise effectuée. <paramref name="ProofValue"/> n'est exigé que si la course a
/// été créée avec une preuve requise — c'est l'agrégat qui tranche.
/// </summary>
public sealed record MarkDeliveredCommand(
    Guid DeliveryId, string? ProofValue = null, Guid? RequiredDriverId = null) : ICommand;

/// <summary>Annule la course. Impossible une fois le colis collecté.</summary>
/// <summary>
/// Annule une course.
///
/// <c>RequiredPartnerId</c> n'est renseigné que par l'API publique. Sans lui, un
/// partenaire pourrait annuler la course d'un autre en présentant son
/// identifiant — l'opération la plus destructrice de toute l'API, et la seule qui
/// ne laisse aucune trace visible côté victime avant que le livreur ne s'arrête.
/// </summary>
public sealed record CancelDeliveryCommand(
    Guid DeliveryId, string? Reason = null, Guid? RequiredPartnerId = null) : ICommand;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES TRANSITIONS D'EXÉCUTION, TOUTES SUR LE MÊME MOULE.
///
/// Chacune fait exactement trois choses : charger, appeler la méthode du domaine,
/// enregistrer. Toute la connaissance — d'où l'on peut venir, ce qui est exigé —
/// vit dans l'agrégat, et un handler qui déciderait quoi que ce soit serait une
/// seconde source de vérité.
///
/// LA REMISE EST À PART, ET C'EST LA SEULE.
///
/// Elle touche DEUX agrégats : la course se termine, et le livreur redevient
/// disponible en incrémentant son compteur de courses — celui qui alimente son
/// score de dispatch. Comme pour l'acceptation, c'est la couche Application qui
/// orchestre, et l'Unit of Work qui garantit que les deux partent ensemble.
///
/// Si l'on oubliait de libérer le livreur, il resterait « en course » pour
/// toujours : plus aucune proposition, et rien pour le signaler — le dispatch se
/// contenterait de ne jamais le retenir.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class DeliveryProgressCommandHandler
    : ICommandHandler<MarkArrivedAtPickupCommand>,
      ICommandHandler<MarkPickedUpCommand>,
      ICommandHandler<MarkInTransitCommand>,
      ICommandHandler<MarkArrivedAtDropoffCommand>,
      ICommandHandler<MarkDeliveredCommand>,
      ICommandHandler<CancelDeliveryCommand>
{
    private readonly IDeliveryRepository _deliveries;
    private readonly IDriverRepository _drivers;
    private readonly IDeliveryPayoutSettings _payout;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public DeliveryProgressCommandHandler(
        IDeliveryRepository deliveries,
        IDriverRepository drivers,
        IDeliveryPayoutSettings payout,
        IDeliveryUnitOfWork unitOfWork)
    {
        _deliveries = deliveries;
        _drivers = drivers;
        _payout = payout;
        _unitOfWork = unitOfWork;
    }

    public Task<Result> Handle(MarkArrivedAtPickupCommand c, CancellationToken ct)
        => MutateAsync(c.DeliveryId, c.RequiredDriverId, d => d.MarkArrivedAtPickup(), ct);

    public Task<Result> Handle(MarkPickedUpCommand c, CancellationToken ct)
        => MutateAsync(c.DeliveryId, c.RequiredDriverId, d => d.MarkPickedUp(), ct);

    public Task<Result> Handle(MarkInTransitCommand c, CancellationToken ct)
        => MutateAsync(c.DeliveryId, c.RequiredDriverId, d => d.MarkInTransit(), ct);

    public Task<Result> Handle(MarkArrivedAtDropoffCommand c, CancellationToken ct)
        => MutateAsync(c.DeliveryId, c.RequiredDriverId, d => d.MarkArrivedAtDropoff(), ct);

    public async Task<Result> Handle(CancelDeliveryCommand c, CancellationToken ct)
    {
        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(c.DeliveryId), ct);

        // « Introuvable » et non « interdit » : un 403 confirmerait au demandeur
        // que la course existe et appartient à quelqu'un d'autre. Voir le même
        // raisonnement, développé, dans DeliveryQueryHandler.
        if (delivery is null
            || (c.RequiredPartnerId is not null && delivery.PartnerId != c.RequiredPartnerId))
        {
            return NotFound();
        }

        var cancelled = delivery.Cancel(c.Reason);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Remise : la course se ferme ET le livreur se libère.</summary>
    public async Task<Result> Handle(MarkDeliveredCommand command, CancellationToken cancellationToken)
    {
        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(command.DeliveryId), cancellationToken);
        if (delivery is null)
        {
            return NotFound();
        }

        // Même garde que les autres étapes — et c'est ICI qu'elle compte le plus :
        // c'est cette transition qui déclenche le gain du livreur.
        if (command.RequiredDriverId is not null
            && delivery.AssignedDriverId?.Value != command.RequiredDriverId)
        {
            return NotFound();
        }

        // On retient le livreur AVANT la transition : l'agrégat conserve son
        // identifiant après la remise, mais s'appuyer sur cet ordre reviendrait à
        // dépendre d'un détail interne que rien ne garantit.
        var assigned = delivery.AssignedDriverId;

        // Le taux est lu MAINTENANT et figé sur la course. C'est la couche
        // Application qui le connaît : le domaine n'a pas accès aux réglages, et
        // un taux codé en dur exigerait un déploiement pour être renégocié.
        var delivered = delivery.MarkDelivered(command.ProofValue, _payout.DriverShareRate);
        if (delivered.IsFailure)
        {
            // ─────────────────────────────────────────────────────────────────
            // ⚠️ ON ENREGISTRE MÊME QUAND LA REMISE ÉCHOUE.
            //
            // C'EST TOUT L'INTÉRÊT DU COMPTEUR. Le réflexe — « échec, donc on ne
            // sauvegarde pas » — le rendrait purement décoratif : chaque tentative
            // incrémenterait un objet en mémoire aussitôt jeté, et le livreur
            // disposerait de tentatives infinies. Le compteur existerait, les
            // tests passeraient, et la faille resterait entière.
            //
            // On n'enregistre QUE si le compteur a bougé : un échec de transition
            // ne doit pas provoquer d'écriture inutile.
            // ─────────────────────────────────────────────────────────────────
            if (delivered.Error.Code is "delivery.proof.pin_mismatch")
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return delivered;
        }

        if (assigned is { } driverId)
        {
            var driver = await _drivers.GetByIdAsync(driverId, cancellationToken);

            // Un échec ici n'annule PAS la remise : le colis est chez le client,
            // c'est un fait acquis. Le livreur mal libéré est un incident
            // d'exploitation, pas une raison de nier une livraison faite.
            driver?.CompleteMission();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result> MutateAsync(
        Guid deliveryId,
        Guid? requiredDriverId,
        Func<Domain.Deliveries.Delivery, Result> mutate,
        CancellationToken cancellationToken)
    {
        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(deliveryId), cancellationToken);
        if (delivery is null)
        {
            return NotFound();
        }

        // « Introuvable » et non « interdit » : un 403 confirmerait au livreur
        // qu'une course existe et qu'elle est confiée à quelqu'un d'autre. Le
        // livreur légitime, lui, ne voit jamais la différence.
        if (requiredDriverId is not null && delivery.AssignedDriverId?.Value != requiredDriverId)
        {
            return NotFound();
        }

        var result = mutate(delivery);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Result NotFound()
        => Result.Failure(Error.NotFound("delivery.not_found", "Course introuvable."));
}
