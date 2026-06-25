using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Wishlist.Domain.Wishlists;

/// <summary>Identité forte d'une liste d'envies.</summary>
public readonly record struct WishlistId(Guid Value)
{
    public static WishlistId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Produit suivi dans une liste d'envies, avec alertes optionnelles de baisse de
/// prix ou de retour en stock. Entité enfant de Wishlist.
/// </summary>
public sealed class WishlistItem : Entity<Guid>
{
    private WishlistItem()
    {
    }

    internal WishlistItem(Guid id, Guid productId, Guid? offerId, bool priceAlert, bool stockAlert)
        : base(id)
    {
        ProductId = productId;
        OfferId = offerId;
        PriceAlert = priceAlert;
        StockAlert = stockAlert;
        AddedAtUtc = DateTime.UtcNow;
    }

    public Guid ProductId { get; private set; }
    public Guid? OfferId { get; private set; }
    public bool PriceAlert { get; private set; }
    public bool StockAlert { get; private set; }
    public DateTime AddedAtUtc { get; private set; }

    internal void SetAlerts(bool priceAlert, bool stockAlert)
    {
        PriceAlert = priceAlert;
        StockAlert = stockAlert;
    }
}

/// <summary>
/// Liste d'envies d'un acheteur (une par utilisateur). Module léger, levier de
/// rétention. Agrégat racine : possède ses lignes.
/// </summary>
public sealed class Wishlist : AggregateRoot<WishlistId>
{
    private readonly List<WishlistItem> _items = new();

    private Wishlist()
    {
    }

    private Wishlist(WishlistId id, Guid userId)
        : base(id)
    {
        UserId = userId;
    }

    public Guid UserId { get; private set; }
    public IReadOnlyCollection<WishlistItem> Items => _items.AsReadOnly();

    public static Result<Wishlist> Create(Guid userId)
        => userId == Guid.Empty
            ? Error.Validation("wishlist.user_required", "L'utilisateur est obligatoire.")
            : new Wishlist(WishlistId.New(), userId);

    public Result AddItem(Guid productId, Guid? offerId, bool priceAlert, bool stockAlert)
    {
        if (productId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("wishlist.product_required", "Le produit est obligatoire."));
        }

        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null)
        {
            existing.SetAlerts(priceAlert, stockAlert);
            return Result.Success();
        }

        _items.Add(new WishlistItem(Guid.NewGuid(), productId, offerId, priceAlert, stockAlert));
        return Result.Success();
    }

    public Result RemoveItem(Guid productId)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("wishlist.item.not_found", "Produit absent de la liste d'envies."));
        }

        _items.Remove(item);
        return Result.Success();
    }

    public Result SetAlerts(Guid productId, bool priceAlert, bool stockAlert)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("wishlist.item.not_found", "Produit absent de la liste d'envies."));
        }

        item.SetAlerts(priceAlert, stockAlert);
        return Result.Success();
    }
}
