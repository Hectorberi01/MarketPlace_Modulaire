using HBA.Deliveries.Contracts.IntegrationEvents;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Shipping.Application.Shipments.Commands;
using MediatR;

namespace Marketplace.Api.Integration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE RETOUR : LA COURSE FAIT AVANCER L'EXPÉDITION.
///
/// Sans ces handlers, le raccordement ne va que dans un sens : le colis part, le
/// livreur le remet, et l'expédition reste éternellement « prête à enlever ». La
/// commande n'atteint jamais « livrée », l'escrow du vendeur n'est jamais libéré,
/// l'acheteur ne reçoit aucune notification et ne peut pas laisser d'avis. Une
/// moitié de raccordement est pire qu'aucune : elle donne l'illusion que ça marche.
///
/// ⚠️ LA RÉFÉRENCE EST LE SEUL FIL ENTRE LES DEUX MONDES.
///
/// HBA Delivery ne connaît ni expédition, ni commande, ni vendeur — c'est le
/// principe qui le rend vendable à des tiers. Il transporte une chaîne opaque,
/// « SHIP-&lt;guid&gt; », et la renvoie telle quelle. C'est ici, et seulement ici,
/// qu'on sait la relire.
///
/// La fonction de relecture est donc partagée par tous les handlers de ce fichier :
/// deux façons de découper la même chaîne finiraient par diverger, et la divergence
/// se manifesterait par des expéditions qui n'avancent plus, sans erreur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class ShipmentReference
{
    private const string Prefix = "SHIP-";

    public static string For(Guid shipmentId) => $"{Prefix}{shipmentId:N}";

    /// <summary>
    /// Relit une référence. Nulle si elle ne vient pas d'une expédition — ce qui
    /// est le cas NORMAL pour une course d'un partenaire externe : ces
    /// événements-là passent par le même canal et ne nous concernent pas.
    /// </summary>
    public static Guid? Read(string? reference)
        => reference is not null
           && reference.StartsWith(Prefix, StringComparison.Ordinal)
           && Guid.TryParseExact(reference[Prefix.Length..], "N", out var parsed)
            ? parsed
            : null;
}

/// <summary>Le colis a été remis : l'expédition passe « livrée ».</summary>
public sealed class MarkShipmentDeliveredOnDeliveryCompletedHandler
    : IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<MarkShipmentDeliveredOnDeliveryCompletedHandler> _logger;

    public MarkShipmentDeliveredOnDeliveryCompletedHandler(
        ISender sender, ILogger<MarkShipmentDeliveredOnDeliveryCompletedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (ShipmentReference.Read(integrationEvent.Reference) is not { } shipmentId)
        {
            return;
        }

        // ─────────────────────────────────────────────────────────────────────
        // « EXPÉDIÉE » PUIS « LIVRÉE », DANS LE MÊME GESTE.
        //
        // Shipment.MarkDelivered() n'accepte que l'état « expédiée ». Or avec HBA
        // Delivery, il n'y a JAMAIS eu de transporteur externe : l'expédition est
        // passée de « prête à enlever » à « remise au client » sans jamais être
        // marquée expédiée par un humain.
        //
        // On franchit donc les deux marches. Le transporteur déclaré est HBA
        // Delivery lui-même, et le numéro de suivi est celui de la course : c'est
        // exact, et cela rend le suivi cliquable côté acheteur.
        // ─────────────────────────────────────────────────────────────────────
        var shipped = await _sender.Send(
            new MarkShipmentShippedCommand(
                shipmentId,
                Carrier: "HBA Delivery",
                TrackingNumber: integrationEvent.DeliveryId.ToString("N")),
            cancellationToken);

        // Un échec ici n'est PAS bloquant : l'expédition était peut-être déjà
        // expédiée par un autre chemin. Ce qui compte est la marche suivante.
        if (shipped.IsFailure)
        {
            _logger.LogInformation(
                "Expédition {ShipmentId} : passage « expédiée » ignoré ({Code}). On tente « livrée ».",
                shipmentId, shipped.Error.Code);
        }

        var delivered = await _sender.Send(new MarkShipmentDeliveredCommand(shipmentId), cancellationToken);

        if (delivered.IsFailure)
        {
            // Celui-là compte : sans lui l'escrow du vendeur reste bloqué et
            // l'acheteur n'est jamais informé. C'est une intervention humaine.
            _logger.LogError(
                "Expédition {ShipmentId} NON marquée livrée alors que la course {DeliveryId} est terminée "
                + "— {Code} : {Message}. Le règlement du vendeur reste bloqué.",
                shipmentId, integrationEvent.DeliveryId, delivered.Error.Code, delivered.Error.Message);
        }
    }
}

/// <summary>
/// La course a été annulée : l'expédition retourne en préparation.
///
/// Elle n'est PAS annulée. Une course annulée avant collecte veut dire « aucun
/// livreur n'est venu », pas « la commande est annulée » — le colis est toujours
/// emballé sur le comptoir du vendeur, et il faut simplement redemander un
/// livreur. Annuler l'expédition annulerait une vente payée.
/// </summary>
public sealed class ResetShipmentOnDeliveryCancelledHandler
    : IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<ResetShipmentOnDeliveryCancelledHandler> _logger;

    public ResetShipmentOnDeliveryCancelledHandler(
        ISender sender, ILogger<ResetShipmentOnDeliveryCancelledHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (ShipmentReference.Read(integrationEvent.Reference) is not { } shipmentId)
        {
            return;
        }

        _logger.LogWarning(
            "Expédition {ShipmentId} : la course a été annulée ({Reason}). Le colis reste chez le vendeur "
            + "et doit être reproposé à un livreur.",
            shipmentId, integrationEvent.Reason ?? "sans motif");

        // Retour en préparation : le vendeur pourra redéclarer « prêt », ce qui
        // relancera une course. C'est volontairement un geste HUMAIN — relancer
        // automatiquement une course qui vient d'être annulée boucle à l'infini
        // quand la cause de l'annulation est permanente.
        await _sender.Send(new MarkShipmentPreparingCommand(shipmentId), cancellationToken);
    }
}

/// <summary>
/// Aucun livreur n'a accepté la course après le plafond de tentatives.
///
/// ⚠️ VOLONTAIREMENT SANS ACTION AUTOMATIQUE.
///
/// La course reste reprenable côté Deliveries, et l'expédition reste « prête à
/// enlever » : rien n'est cassé, mais personne ne vient. Le seul traitement utile
/// est humain — élargir la zone, majorer la course, ou prévenir l'acheteur d'un
/// retard. Ce handler existe donc pour RENDRE LE FAIT VISIBLE, ce qui est déjà
/// plus que ce qui existait.
/// </summary>
public sealed class AlertOnDeliveryNoDriverAvailableHandler
    : IIntegrationEventHandler<DeliveryNoDriverAvailableIntegrationEvent>
{
    private readonly ILogger<AlertOnDeliveryNoDriverAvailableHandler> _logger;

    public AlertOnDeliveryNoDriverAvailableHandler(ILogger<AlertOnDeliveryNoDriverAvailableHandler> logger)
        => _logger = logger;

    public Task HandleAsync(
        DeliveryNoDriverAvailableIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var shipmentId = ShipmentReference.Read(integrationEvent.Reference);

        _logger.LogError(
            "HBA Delivery : AUCUN LIVREUR pour la course {DeliveryId} après {Attempts} tentatives "
            + "(expédition {ShipmentId}). Le colis est prêt et personne ne vient — intervention "
            + "d'exploitation requise.",
            integrationEvent.DeliveryId,
            integrationEvent.Attempts,
            shipmentId?.ToString() ?? "externe");

        return Task.CompletedTask;
    }
}
