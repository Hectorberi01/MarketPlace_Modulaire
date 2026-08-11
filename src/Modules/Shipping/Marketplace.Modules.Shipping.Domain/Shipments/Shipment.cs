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

    /// <summary>
    /// Quand le vendeur a déclaré le colis prêt. Sert à mesurer le délai de
    /// préparation — le seul délai que le vendeur maîtrise, et donc le seul qu'on
    /// puisse lui reprocher.
    /// </summary>
    public DateTime? ReadyForPickupAtUtc { get; private set; }

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
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE COLIS EST EMBALLÉ ET ATTEND UN LIVREUR.
    ///
    /// C'est le geste vendeur qui déclenche la recherche d'un coursier. L'état
    /// est distinct de « en préparation » : celui-ci dit qu'on a commencé,
    /// celui-là qu'on a fini. Le dispatch ne peut se fier qu'au second — sinon un
    /// livreur part vers une boutique où l'on est encore en train d'emballer,
    /// attend, et finit par refuser la course.
    ///
    /// ⚠️ AUCUN ÉVÉNEMENT N'EST LEVÉ SI L'EXPÉDITION Y EST DÉJÀ.
    ///
    /// Un vendeur appuie deux fois sur un bouton — c'est la règle, pas
    /// l'exception, sur un téléphone en zone de mauvaise couverture. Sans cette
    /// garde, chaque appui produirait un événement, donc une tentative de course
    /// supplémentaire. La création de course est certes idempotente par
    /// référence, mais s'appuyer sur la garde AVAL pour excuser un événement
    /// dupliqué en AMONT, c'est laisser la duplication se propager jusqu'au
    /// premier consommateur qui n'aura pas pensé à s'en protéger.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result MarkReadyForPickup()
    {
        if (Status is ShipmentStatus.ReadyForPickup)
        {
            return Result.Success();
        }

        if (Status is not (ShipmentStatus.Pending or ShipmentStatus.Preparing))
        {
            return Result.Failure(Error.Conflict(
                "shipping.invalid_transition",
                "Seule une expédition en attente ou en préparation peut être déclarée prête à enlever."));
        }

        Status = ShipmentStatus.ReadyForPickup;
        ReadyForPickupAtUtc = DateTime.UtcNow;
        Raise(new ShipmentReadyForPickupDomainEvent(Id.Value, OrderId, SellerId, ShipFromLocationId));
        return Result.Success();
    }

    /// <summary>
    /// Marque « expédiée ». [trackingUrl] est le lien cliquable pré-calculé depuis le
    /// transporteur du catalogue (facultatif : null si le transporteur n'a pas de
    /// modèle d'URL, ou si le nom a été saisi librement).
    ///
    /// « Prête à enlever » y mène aussi : c'est le chemin normal quand la course
    /// est confiée à HBA Delivery — le livreur collecte, et l'expédition passe en
    /// transit sans jamais avoir vu de transporteur externe.
    /// </summary>
    public Result MarkShipped(string carrier, string trackingNumber, string? trackingUrl = null)
    {
        if (Status is not (ShipmentStatus.Pending or ShipmentStatus.Preparing or ShipmentStatus.ReadyForPickup))
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
