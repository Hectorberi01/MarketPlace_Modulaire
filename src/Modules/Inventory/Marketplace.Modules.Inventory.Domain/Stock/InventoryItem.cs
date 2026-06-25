using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Inventory.Domain.Common;
using Marketplace.Modules.Inventory.Domain.Stock.Events;

namespace Marketplace.Modules.Inventory.Domain.Stock;

public readonly record struct InventoryItemId(Guid Value)
{
    public static InventoryItemId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Stock d'un SKU sur une localisation donnée. Disponible = OnHand − Reserved
/// (cf. dossier, InventoryItem). Agrégat racine : possède ses réservations.
/// </summary>
public sealed class InventoryItem : AggregateRoot<InventoryItemId>
{
    private readonly List<StockReservation> _reservations = new();

    private InventoryItem()
    {
    }

    private InventoryItem(InventoryItemId id, Sku sku, Guid locationId, int onHand, int reorderThreshold)
        : base(id)
    {
        Sku = sku;
        LocationId = locationId;
        OnHand = onHand;
        ReorderThreshold = reorderThreshold;

        Raise(new InventoryItemCreatedDomainEvent(id.Value, sku.Value, locationId));
    }

    public Sku Sku { get; private set; } = default!;
    public Guid LocationId { get; private set; }
    public int OnHand { get; private set; }
    public int ReorderThreshold { get; private set; }

    public IReadOnlyCollection<StockReservation> Reservations => _reservations.AsReadOnly();

    /// <summary>Quantité réservée (somme des réservations en cours).</summary>
    public int Reserved => _reservations.Sum(r => r.Quantity);

    /// <summary>Quantité réellement disponible à la vente.</summary>
    public int Available => OnHand - Reserved;

    public bool IsLowStock => Available <= ReorderThreshold;

    public static Result<InventoryItem> Create(Sku sku, Guid locationId, int onHand, int reorderThreshold)
    {
        if (locationId == Guid.Empty)
        {
            return Error.Validation("inventory.item.location_required", "La localisation est obligatoire.");
        }

        if (onHand < 0)
        {
            return Error.Validation("inventory.item.onhand_negative", "Le stock physique ne peut pas être négatif.");
        }

        if (reorderThreshold < 0)
        {
            return Error.Validation("inventory.item.threshold_negative", "Le seuil de réapprovisionnement ne peut pas être négatif.");
        }

        return new InventoryItem(InventoryItemId.New(), sku, locationId, onHand, reorderThreshold);
    }

    /// <summary>Entrée de stock (réception fournisseur / vendeur).</summary>
    public Result Receive(int quantity)
    {
        if (quantity <= 0)
        {
            return Result.Failure(Error.Validation("inventory.item.quantity_invalid", "La quantité doit être positive."));
        }

        OnHand += quantity;
        return Result.Success();
    }

    /// <summary>Réserve du stock pour une commande, si disponible.</summary>
    public Result Reserve(Guid orderId, int quantity, DateTime expiresAtUtc)
    {
        if (quantity <= 0)
        {
            return Result.Failure(Error.Validation("inventory.item.quantity_invalid", "La quantité doit être positive."));
        }

        if (Available < quantity)
        {
            return Result.Failure(Error.Conflict("inventory.item.insufficient_stock", "Stock insuffisant pour la réservation."));
        }

        _reservations.Add(new StockReservation(Guid.NewGuid(), orderId, quantity, expiresAtUtc));
        Raise(new StockReservedDomainEvent(Id.Value, Sku.Value, orderId, quantity));

        if (Available == 0)
        {
            Raise(new StockDepletedDomainEvent(Id.Value, Sku.Value, LocationId));
        }

        return Result.Success();
    }

    /// <summary>Libère les réservations d'une commande (paiement échoué / annulation).</summary>
    public Result ReleaseReservation(Guid orderId)
    {
        _reservations.RemoveAll(r => r.OrderId == orderId);
        return Result.Success();
    }

    /// <summary>Confirme la vente : décrémente le stock physique et solde les réservations.</summary>
    public Result ConfirmReservation(Guid orderId)
    {
        var reserved = _reservations.Where(r => r.OrderId == orderId).Sum(r => r.Quantity);
        if (reserved == 0)
        {
            return Result.Failure(Error.NotFound("inventory.item.reservation_not_found", "Aucune réservation pour cette commande."));
        }

        OnHand -= reserved;
        _reservations.RemoveAll(r => r.OrderId == orderId);
        return Result.Success();
    }

    /// <summary>Ajuste le stock physique (inventaire, casse, retour).</summary>
    public Result AdjustOnHand(int delta)
    {
        if (OnHand + delta < Reserved)
        {
            return Result.Failure(Error.Conflict("inventory.item.adjust_below_reserved", "L'ajustement passerait le stock sous le réservé."));
        }

        OnHand += delta;
        return Result.Success();
    }

    public Result SetReorderThreshold(int reorderThreshold)
    {
        if (reorderThreshold < 0)
        {
            return Result.Failure(Error.Validation("inventory.item.threshold_negative", "Le seuil ne peut pas être négatif."));
        }

        ReorderThreshold = reorderThreshold;
        return Result.Success();
    }
}
