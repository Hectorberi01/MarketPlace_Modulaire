using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Shipping.Domain.Shipments.Events;

namespace Marketplace.Modules.Shipping.Domain.Shipments;

/// <summary>Données d'une ligne à expédier (SKU + quantité).</summary>
public sealed record ShipmentLineDraft(string Sku, int Quantity);

/// <summary>
/// Expédition d'un colis pour un vendeur d'une commande (place de marché multi-
/// vendeur : une commande = potentiellement plusieurs expéditions, une par
/// vendeur / lieu d'expédition). Agrégat racine : possède ses lignes.
/// </summary>
public sealed class Shipment : AggregateRoot<ShipmentId>
{
    private readonly List<ShipmentItem> _items = new();

    private Shipment()
    {
    }

    private Shipment(ShipmentId id, Guid orderId, Guid sellerId, Guid buyerId, Guid shipFromLocationId)
        : base(id)
    {
        OrderId = orderId;
        SellerId = sellerId;
        BuyerId = buyerId;
        ShipFromLocationId = shipFromLocationId;
        Status = ShipmentStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid OrderId { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid ShipFromLocationId { get; private set; }
    public ShipmentStatus Status { get; private set; }
    public string? Carrier { get; private set; }
    public string? TrackingNumber { get; private set; }

    /// <summary>Lien de suivi cliquable, dérivé du transporteur du catalogue (null si aucun modèle).</summary>
    public string? TrackingUrl { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ShippedAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }

    public IReadOnlyCollection<ShipmentItem> Items => _items.AsReadOnly();

    public static Result<Shipment> Create(
        Guid orderId, Guid sellerId, Guid buyerId, Guid shipFromLocationId, IEnumerable<ShipmentLineDraft> lines)
    {
        if (orderId == Guid.Empty || sellerId == Guid.Empty)
        {
            return Error.Validation("shipping.refs_required", "Commande et vendeur sont obligatoires.");
        }

        var lineList = lines?.Where(l => l.Quantity > 0).ToList() ?? new List<ShipmentLineDraft>();
        if (lineList.Count == 0)
        {
            return Error.Validation("shipping.no_lines", "Une expédition doit comporter au moins une ligne.");
        }

        var shipment = new Shipment(ShipmentId.New(), orderId, sellerId, buyerId, shipFromLocationId);
        foreach (var line in lineList)
        {
            shipment._items.Add(new ShipmentItem(Guid.NewGuid(), line.Sku, line.Quantity));
        }

        shipment.Raise(new ShipmentCreatedDomainEvent(shipment.Id.Value, orderId, sellerId));
        return shipment;
    }

    public Result MarkPreparing()
    {
        if (Status != ShipmentStatus.Pending)
        {
            return Result.Failure(Error.Conflict("shipping.invalid_transition", "L'expédition n'est pas en attente de préparation."));
        }

        Status = ShipmentStatus.Preparing;
        return Result.Success();
    }

    /// <summary>
    /// Marque « expédiée ». [trackingUrl] est le lien cliquable pré-calculé depuis le
    /// transporteur du catalogue (facultatif : null si le transporteur n'a pas de
    /// modèle d'URL, ou si le nom a été saisi librement).
    /// </summary>
    public Result MarkShipped(string carrier, string trackingNumber, string? trackingUrl = null)
    {
        if (Status is not (ShipmentStatus.Pending or ShipmentStatus.Preparing))
        {
            return Result.Failure(Error.Conflict("shipping.invalid_transition", "L'expédition ne peut pas être expédiée dans cet état."));
        }

        if (string.IsNullOrWhiteSpace(carrier) || string.IsNullOrWhiteSpace(trackingNumber))
        {
            return Result.Failure(Error.Validation("shipping.tracking_required", "Transporteur et numéro de suivi sont obligatoires."));
        }

        Status = ShipmentStatus.Shipped;
        Carrier = carrier.Trim();
        TrackingNumber = trackingNumber.Trim();
        TrackingUrl = string.IsNullOrWhiteSpace(trackingUrl) ? null : trackingUrl.Trim();
        ShippedAtUtc = DateTime.UtcNow;
        Raise(new ShipmentShippedDomainEvent(Id.Value, OrderId, Carrier, TrackingNumber));
        return Result.Success();
    }

    public Result MarkDelivered()
    {
        if (Status != ShipmentStatus.Shipped)
        {
            return Result.Failure(Error.Conflict("shipping.invalid_transition", "Seule une expédition en transit peut être livrée."));
        }

        Status = ShipmentStatus.Delivered;
        DeliveredAtUtc = DateTime.UtcNow;
        Raise(new ShipmentDeliveredDomainEvent(Id.Value, OrderId, SellerId));
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status is ShipmentStatus.Delivered or ShipmentStatus.Cancelled)
        {
            return Result.Failure(Error.Conflict("shipping.not_cancellable", "L'expédition n'est plus annulable."));
        }

        Status = ShipmentStatus.Cancelled;
        return Result.Success();
    }
}
