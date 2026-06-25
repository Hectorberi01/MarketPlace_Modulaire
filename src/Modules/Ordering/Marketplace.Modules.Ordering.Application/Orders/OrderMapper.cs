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
            l.UnitBasePrice, l.SellerDiscount, l.PlatformDiscount, l.FinalUnitPrice, l.LineTotal)).ToList());
}
