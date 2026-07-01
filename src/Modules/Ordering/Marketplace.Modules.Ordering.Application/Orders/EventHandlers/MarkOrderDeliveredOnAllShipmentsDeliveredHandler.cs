using MediatR;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Application.Orders.Commands;
using Marketplace.Modules.Shipping.Contracts;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Ordering.Application.Orders.EventHandlers;

/// <summary>
/// Étape finale du Saga côté commande : à la livraison d'une expédition, on vérifie
/// si TOUTES les expéditions de la commande sont livrées ; si oui, on marque la
/// commande « livrée ». Cette transition publie OrderDelivered, qui déclenche en
/// aval la libération de l'escrow (Payments) et le payout vendeur (Settlement).
///
/// Idempotent : sans effet si la commande n'est pas encore confirmée ou déjà livrée
/// (MarkDelivered renvoie alors un échec qu'on ignore volontairement).
/// </summary>
public sealed class MarkOrderDeliveredOnAllShipmentsDeliveredHandler : IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>
{
    private readonly IShippingModuleApi _shipping;
    private readonly ISender _sender;

    public MarkOrderDeliveredOnAllShipmentsDeliveredHandler(IShippingModuleApi shipping, ISender sender)
    {
        _shipping = shipping;
        _sender = sender;
    }

    public async Task HandleAsync(ShipmentDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var shipments = await _shipping.GetShipmentsByOrderAsync(integrationEvent.OrderId, cancellationToken);
        if (shipments.Count == 0)
        {
            return;
        }

        var allDelivered = shipments.All(s => string.Equals(s.Status, "Delivered", StringComparison.OrdinalIgnoreCase));
        if (!allDelivered)
        {
            return;
        }

        await _sender.Send(new MarkOrderDeliveredCommand(integrationEvent.OrderId), cancellationToken);
    }
}
