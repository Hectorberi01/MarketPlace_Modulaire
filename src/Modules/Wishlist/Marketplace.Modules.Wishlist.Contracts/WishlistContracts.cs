namespace Marketplace.Modules.Wishlist.Contracts;

/// <summary>Produit suivi dans une liste d'envies.</summary>
public sealed record WishlistItemSummary(Guid ProductId, Guid? OfferId, bool PriceAlert, bool StockAlert, DateTime AddedAtUtc);

/// <summary>Vue publique d'une liste d'envies.</summary>
public sealed record WishlistSummary(Guid Id, Guid UserId, IReadOnlyList<WishlistItemSummary> Items);
