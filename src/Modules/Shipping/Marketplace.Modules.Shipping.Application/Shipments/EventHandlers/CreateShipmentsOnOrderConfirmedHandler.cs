using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Shipping.Application.Abstractions;
using Marketplace.Modules.Shipping.Domain.Shipments;

namespace Marketplace.Modules.Shipping.Application.Shipments.EventHandlers;

/// <summary>
/// À la confirmation d'une commande (paiement encaissé), crée une expédition par
/// vendeur / lieu d'expédition à partir des lignes de la commande (lues via les
/// Contracts d'Ordering). Idempotent : ne recrée pas si déjà fait.
/// </summary>
public sealed class CreateShipmentsOnOrderConfirmedHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly IShipmentRepository _shipmentRepository;
    private readonly IOrderingModuleApi _orderingModuleApi;
    private readonly IShippingUnitOfWork _unitOfWork;

    public CreateShipmentsOnOrderConfirmedHandler(
        IShipmentRepository shipmentRepository,
        IOrderingModuleApi orderingModuleApi,
        IShippingUnitOfWork unitOfWork)
    {
        _shipmentRepository = shipmentRepository;
        _orderingModuleApi = orderingModuleApi;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await _shipmentRepository.ExistsForOrderAsync(integrationEvent.OrderId, cancellationToken))
        {
            return;
        }

        var order = await _orderingModuleApi.GetOrderAsync(integrationEvent.OrderId, cancellationToken);
        if (order is null || order.Lines.Count == 0)
        {
            return;
        }

        var groups = order.Lines.GroupBy(l => new { l.SellerId, l.ShipFromLocationId });
        foreach (var group in groups)
        {
            var lines = group.Select(l => new ShipmentLineDraft(l.Sku, l.Quantity));
            var result = Shipment.Create(order.Id, group.Key.SellerId, order.BuyerId, group.Key.ShipFromLocationId, lines);
            if (result.IsSuccess)
            {
                await _shipmentRepository.AddAsync(result.Value, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
