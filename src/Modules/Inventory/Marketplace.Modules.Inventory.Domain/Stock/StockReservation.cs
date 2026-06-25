using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Inventory.Domain.Stock;

/// <summary>
/// Réservation temporaire de stock à la commande, libérée si le paiement échoue
/// (cf. dossier, StockReservation). Entité enfant de l'agrégat InventoryItem.
/// </summary>
public sealed class StockReservation : Entity<Guid>
{
    private StockReservation()
    {
    }

    internal StockReservation(Guid id, Guid orderId, int quantity, DateTime expiresAtUtc)
        : base(id)
    {
        OrderId = orderId;
        Quantity = quantity;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid OrderId { get; private set; }
    public int Quantity { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
}
