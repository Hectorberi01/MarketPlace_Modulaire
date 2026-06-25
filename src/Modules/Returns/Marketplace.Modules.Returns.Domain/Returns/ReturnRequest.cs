using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Returns.Domain.Returns.Events;

namespace Marketplace.Modules.Returns.Domain.Returns;

/// <summary>
/// Demande de retour (RMA). Couplée à Ordering pour valider la ligne, et à
/// Payments/Settlement à travers ses events : le remboursement impacte l'escrow
/// et le payout du vendeur. Cycle : Requested → Approved/Rejected → Received →
/// Refunded.
/// </summary>
public sealed class ReturnRequest : AggregateRoot<ReturnRequestId>
{
    private ReturnRequest()
    {
    }

    private ReturnRequest(
        ReturnRequestId id, Guid orderId, Guid offerId, Guid buyerId, Guid sellerId, ReturnReason reason, string currency)
        : base(id)
    {
        OrderId = orderId;
        OfferId = offerId;
        BuyerId = buyerId;
        SellerId = sellerId;
        Reason = reason;
        Currency = currency;
        Status = ReturnStatus.Requested;
        CreatedAtUtc = DateTime.UtcNow;

        Raise(new ReturnRequestedDomainEvent(id.Value, orderId, offerId, buyerId));
    }

    public Guid OrderId { get; private set; }
    public Guid OfferId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid SellerId { get; private set; }
    public ReturnReason Reason { get; private set; }
    public ReturnStatus Status { get; private set; }
    public string Currency { get; private set; } = default!;
    public decimal? RefundAmount { get; private set; }
    public Guid? RefundId { get; private set; }
    public string? Carrier { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }

    public static Result<ReturnRequest> Create(
        Guid orderId, Guid offerId, Guid buyerId, Guid sellerId, ReturnReason reason, string currency)
    {
        if (orderId == Guid.Empty || offerId == Guid.Empty || buyerId == Guid.Empty)
        {
            return Error.Validation("returns.refs_required", "Commande, ligne et acheteur sont obligatoires.");
        }

        return new ReturnRequest(ReturnRequestId.New(), orderId, offerId, buyerId, sellerId, reason, currency.Trim().ToUpperInvariant());
    }

    public Result Approve()
    {
        if (Status != ReturnStatus.Requested)
        {
            return Result.Failure(Error.Conflict("returns.not_requested", "Seule une demande en attente peut être approuvée."));
        }

        Status = ReturnStatus.Approved;
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        if (Status != ReturnStatus.Requested)
        {
            return Result.Failure(Error.Conflict("returns.not_requested", "Seule une demande en attente peut être rejetée."));
        }

        Status = ReturnStatus.Rejected;
        RejectionReason = reason;
        ResolvedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Renseigne le colis retour (après approbation).</summary>
    public Result AddReturnTracking(string carrier, string trackingNumber)
    {
        if (Status is not (ReturnStatus.Approved or ReturnStatus.Received))
        {
            return Result.Failure(Error.Conflict("returns.not_approved", "Le retour n'est pas approuvé."));
        }

        Carrier = carrier.Trim();
        TrackingNumber = trackingNumber.Trim();
        return Result.Success();
    }

    public Result MarkReceived()
    {
        if (Status != ReturnStatus.Approved)
        {
            return Result.Failure(Error.Conflict("returns.not_approved", "Le retour doit être approuvé avant réception."));
        }

        Status = ReturnStatus.Received;
        return Result.Success();
    }

    /// <summary>Déclenche le remboursement (impacte l'escrow / le payout vendeur).</summary>
    public Result Refund(decimal amount)
    {
        if (Status != ReturnStatus.Received)
        {
            return Result.Failure(Error.Conflict("returns.not_received", "Le retour doit être réceptionné avant remboursement."));
        }

        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation("returns.amount_invalid", "Le montant remboursé doit être positif."));
        }

        Status = ReturnStatus.Refunded;
        RefundAmount = amount;
        RefundId = Guid.NewGuid();
        ResolvedAtUtc = DateTime.UtcNow;
        Raise(new ReturnRefundedDomainEvent(Id.Value, OrderId, SellerId, amount, Currency));
        return Result.Success();
    }
}
