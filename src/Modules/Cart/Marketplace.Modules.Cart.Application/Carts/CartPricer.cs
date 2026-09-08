using Marketplace.Modules.Cart.Contracts;
using Marketplace.Modules.Ordering.Contracts;
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
        CartAggregate cart,
        IPricingModuleApi pricing,
        IOrderingModuleApi ordering,
        CancellationToken cancellationToken)
    {
        var lines = new List<CartLineSummary>(cart.Items.Count);
        decimal subtotal = 0m, totalSeller = 0m, totalPlatform = 0m, grand = 0m;

        // ═════════════════════════════════════════════════════════════════════════
        // CES DEUX VALEURS ÉTAIENT CODÉES EN DUR — `Code: null` ET `IsFirstOrder: false`.
        //
        // Conséquence, et elle était totale : `Promotion.AppliesTo()` rejette toute promo
        // qui exige un code lorsque le code fourni ne correspond pas — et `null` ne
        // correspond jamais. `PromotionConditions.AreMet()` rejette toute promo
        // « première commande » lorsque `isFirstOrder` est faux — et il l'était toujours.
        //
        // Autrement dit, DEUX familles entières de promotions étaient inapplicables : le
        // back-office pouvait les créer, l'admin les activer, et aucun acheteur au monde
        // n'en aurait jamais vu la couleur. Le moteur tournait, mais à vide.
        //
        // Le coût de l'appel à Ordering est un EXISTS sur un index (BuyerId) — et il ne
        // sert qu'à répondre « cet acheteur a-t-il déjà commandé ? ». On l'évalue UNE fois
        // pour le panier, pas une fois par ligne : c'est une propriété de l'acheteur, pas
        // de l'article.
        // ═════════════════════════════════════════════════════════════════════════
        var isFirstOrder = !await ordering.HasPlacedOrderAsync(cart.BuyerId, cancellationToken);

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
                Code: cart.PromotionCode,
                IsFirstOrder: isFirstOrder);

            var b = await pricing.CalculatePriceAsync(request, cancellationToken);

            var lineSeller = b.SellerDiscount * item.Quantity;
            var linePlatform = b.PlatformDiscount * item.Quantity;
            var lineTotal = b.FinalAmount * item.Quantity;

            lines.Add(new CartLineSummary(
                item.OfferId, item.ProductId, item.CategoryId, item.SellerId, item.Sku,
                item.ShipFromLocationId, item.Quantity, item.UnitBaseAmount,
                b.SellerDiscount, b.PlatformDiscount, b.FinalAmount, lineTotal, item.Currency,
                item.CommissionRate, item.ProviderFeeRate));

            subtotal += item.UnitBaseAmount * item.Quantity;
            totalSeller += lineSeller;
            totalPlatform += linePlatform;
            grand += lineTotal;
        }

        return new CartSummary(
            cart.Id.Value, cart.BuyerId, cart.Currency, cart.Status.ToString(),
            lines, subtotal, totalSeller, totalPlatform, grand, cart.PromotionCode);
    }
}
