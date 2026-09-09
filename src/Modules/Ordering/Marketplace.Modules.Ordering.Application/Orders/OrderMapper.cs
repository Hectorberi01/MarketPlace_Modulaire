using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Domain.Orders;

namespace Marketplace.Modules.Ordering.Application.Orders;

internal static class OrderMapper
{
    public static OrderSummary ToSummary(Order order) => new(
        order.Id.Value,
        order.BuyerId,
        order.CartId,
        order.Currency,
        order.Status.ToString(),
        order.CreatedAtUtc,
        order.Subtotal,
        order.TotalSellerDiscount,
        order.TotalPlatformDiscount,
        order.GrandTotal,
        order.Lines.Select(l => new OrderLineSummary(
            l.OfferId, l.ProductId, l.SellerId, l.Sku, l.ShipFromLocationId, l.Quantity,
            l.UnitBasePrice, l.SellerDiscount, l.PlatformDiscount, l.FinalUnitPrice, l.LineTotal,
            l.CommissionRate, l.ProviderFeeRate)).ToList(),
        order.HasShippingAddress
            ? new OrderShippingAddressSummary(
                order.ShipToLabel, order.ShipToRecipient,
                order.ShipToCommuneCode, order.ShipToCommuneName,
                order.ShipToQuartier, order.ShipToLandmark, order.ShipToLine1,
                order.ShipToCountryCode,
                order.ShipToLatitude, order.ShipToLongitude,
                order.ShipToPhone)
            : null,
        order.ShippingFee,
        order.WalletApplied,
        order.DeliveredAtUtc);
}
