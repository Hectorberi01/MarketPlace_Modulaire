namespace Marketplace.Modules.Cart.Application.Carts;

/// <summary>Clés de cache du panier (cache-aside via ICacheService / Redis).</summary>
internal static class CartCacheKeys
{
    public static string Active(Guid buyerId) => $"cart:active:{buyerId}";
}
