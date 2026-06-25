using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Ordering.Domain.Orders.Events;

namespace Marketplace.Modules.Ordering.Domain.Orders;

/// <summary>
/// Commande multi-vendeur. Fige le prix de chaque ligne (prix de base +
/// réductions par financeur) au moment de l'achat. Son cycle de vie est piloté
/// par un Saga d'orchestration : réservation du stock, paiement, confirmation,
/// avec compensations. Agrégat racine : possède ses lignes.
/// </summary>
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = new();

    private Order()
    {
    }

    private Order(OrderId id, Guid buyerId, Guid cartId, string currency)
        : base(id)
    {
        BuyerId = buyerId;
        CartId = cartId;
        Currency = currency;
        Status = OrderStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid BuyerId { get; private set; }
    public Guid CartId { get; private set; }
    public string Currency { get; private set; } = default!;
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal TotalSellerDiscount { get; private set; }
    public decimal TotalPlatformDiscount { get; private set; }
    public decimal GrandTotal { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public static Result<Order> Create(Guid buyerId, Guid cartId, string currency, IEnumerable<OrderLineDraft> drafts)
    {
        if (buyerId == Guid.Empty)
        {
            return Error.Validation("ordering.buyer_required", "L'acheteur est obligatoire.");
        }

        var draftList = drafts?.ToList() ?? new List<OrderLineDraft>();
        if (draftList.Count == 0)
        {
            return Error.Validation("ordering.no_lines", "Une commande doit comporter au moins une ligne.");
        }

        if (draftList.Any(d => d.Quantity <= 0))
        {
            return Error.Validation("ordering.line_quantity_invalid", "Chaque ligne doit avoir une quantité positive.");
        }

        var order = new Order(OrderId.New(), buyerId, cartId, currency.Trim().ToUpperInvariant());

        foreach (var draft in draftList)
        {
            order._lines.Add(new OrderLine(Guid.NewGuid(), draft));
        }

        order.RecomputeTotals();
        return order;
    }

    private void RecomputeTotals()
    {
        Subtotal = _lines.Sum(l => l.UnitBasePrice * l.Quantity);
        TotalSellerDiscount = _lines.Sum(l => l.SellerDiscount * l.Quantity);
        TotalPlatformDiscount = _lines.Sum(l => l.PlatformDiscount * l.Quantity);
        GrandTotal = _lines.Sum(l => l.LineTotal);
    }

    /// <summary>Stock réservé : la commande attend le paiement. Émet OrderPlaced.</summary>
    public Result MarkAwaitingPayment()
    {
        if (Status != OrderStatus.Pending)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "Transition invalide vers « en attente de paiement »."));
        }

        Status = OrderStatus.AwaitingPayment;
        Raise(new OrderPlacedDomainEvent(Id.Value, BuyerId, CartId, GrandTotal, Currency));
        return Result.Success();
    }

    /// <summary>Paiement encaissé.</summary>
    public Result MarkPaid()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "Aucun paiement attendu dans cet état."));
        }

        Status = OrderStatus.Paid;
        return Result.Success();
    }

    /// <summary>Réservations soldées : la commande est confirmée.</summary>
    public Result Confirm()
    {
        if (Status != OrderStatus.Paid)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "La commande doit être payée avant confirmation."));
        }

        Status = OrderStatus.Confirmed;
        Raise(new OrderConfirmedDomainEvent(Id.Value, BuyerId));
        return Result.Success();
    }

    /// <summary>
    /// Livraison confirmée (toutes les expéditions reçues). Déclenche, en aval,
    /// la libération de l'escrow et le payout vendeur (Saga, étape finale).
    /// </summary>
    public Result MarkDelivered()
    {
        if (Status != OrderStatus.Confirmed)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "Seule une commande confirmée peut être marquée livrée."));
        }

        Status = OrderStatus.Delivered;
        Raise(new OrderDeliveredDomainEvent(Id.Value, BuyerId));
        return Result.Success();
    }

    /// <summary>Annulation (réservations à libérer en compensation).</summary>
    public Result Cancel(string reason)
    {
        if (Status is OrderStatus.Confirmed or OrderStatus.Cancelled or OrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict("ordering.not_cancellable", "La commande n'est plus annulable."));
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        Raise(new OrderCancelledDomainEvent(Id.Value, BuyerId, reason));
        return Result.Success();
    }

    /// <summary>Échec du Saga (ex. stock indisponible) avant paiement.</summary>
    public void Fail(string reason)
    {
        Status = OrderStatus.Failed;
        CancellationReason = reason;
    }
}
