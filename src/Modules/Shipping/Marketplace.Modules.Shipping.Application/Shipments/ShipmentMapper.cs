using Marketplace.Modules.Shipping.Contracts;
using Marketplace.Modules.Shipping.Domain.Shipments;

namespace Marketplace.Modules.Shipping.Application.Shipments;

internal static class ShipmentMapper
{
    public static ShipmentSummary ToSummary(Shipment s) => new(
        s.Id.Value,
        s.OrderId,
        s.SellerId,
        s.BuyerId,
        s.ShipFromLocationId,
        s.Status.ToString(),
        s.Carrier,
        s.TrackingNumber,
        s.CreatedAtUtc,
        s.ShippedAtUtc,
        s.DeliveredAtUtc,
        s.Items.Select(i => new ShipmentItemSummary(i.Sku, i.Quantity)).ToList());
}
