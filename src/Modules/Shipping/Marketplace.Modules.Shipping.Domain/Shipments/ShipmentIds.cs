namespace Marketplace.Modules.Shipping.Domain.Shipments;

/// <summary>Identité forte d'une expédition.</summary>
public readonly record struct ShipmentId(Guid Value)
{
    public static ShipmentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Statut d'une expédition (livraison d'un colis vendeur).</summary>
public enum ShipmentStatus
{
    Pending = 0,
    Preparing = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4
}
