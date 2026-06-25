using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Shipping.Domain.Shipments;

/// <summary>Ligne d'une expédition : un SKU et sa quantité. Entité enfant de Shipment.</summary>
public sealed class ShipmentItem : Entity<Guid>
{
    private ShipmentItem()
    {
    }

    internal ShipmentItem(Guid id, string sku, int quantity)
        : base(id)
    {
        Sku = sku;
        Quantity = quantity;
    }

    public string Sku { get; private set; } = default!;
    public int Quantity { get; private set; }
}
