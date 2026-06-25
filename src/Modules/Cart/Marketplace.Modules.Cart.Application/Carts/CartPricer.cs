using Marketplace.Modules.Cart.Contracts;
using Marketplace.Modules.Pricing.Contracts;
using CartAggregate = Marketplace.Modules.Cart.Domain.Carts.Cart;

namespace Marketplace.Modules.Cart.Application.Carts;

/// <summary>
/// Valorise un panier : pour chaque ligne, appelle Pricing pour obtenir le prix
/// effectif (avec la trace des financeurs), puis agrège les totaux. Le panier ne
/// stocke jamais ces prix — ils sont recalculés à chaque lecture.
/// </summary>
internal static class CartPricer
{
    public static async Task<CartSummary> PriceAsync(
        CartAggregate cart, IPricingModuleApi pricing, CancellationToken cancellationToken)
    {
        var lines = new List<CartLineSummary>(cart.Items.Count);
        decimal subtotal = 0m, totalSeller = 0m, totalPlatform = 0m, grand = 0m;

        foreach (var item in cart.Items)
        {
            var request = new PriceRequest(
                BaseAmount: item.UnitBaseAmount,
                Currency: item.Currency,
                ProductId: item.ProductId,
                CategoryId: item.CategoryId,
                SellerId: item.SellerId,
                Quantity: item.Quantity,
                Subtotal: item.UnitBaseAmount * item.Quantity,
                Code: null,
                IsFirstOrder: false);

            var b = await pricing.CalculatePriceAsync(request, cancellationToken);

            var lineSeller = b.SellerDiscount * item.Quantity;
            var linePlatform = b.PlatformDiscount * item.Quantity;
            var lineTotal = b.FinalAmount * item.Quantity;

            lines.Add(new CartLineSummary(
                item.OfferId, item.ProductId, item.CategoryId, item.SellerId, item.Sku,
                item.ShipFromLocationId, item.Quantity, item.UnitBaseAmount,
                b.SellerDiscount, b.PlatformDiscount, b.FinalAmount, lineTotal, item.Currency));

            subtotal += item.UnitBaseAmount * item.Quantity;
            totalSeller += lineSeller;
            totalPlatform += linePlatform;
            grand += lineTotal;
        }

        return new CartSummary(
            cart.Id.Value, cart.BuyerId, cart.Currency, cart.Status.ToString(),
            lines, subtotal, totalSeller, totalPlatform, grand);
    }
}
