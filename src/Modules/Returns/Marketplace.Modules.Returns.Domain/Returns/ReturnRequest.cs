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

    /// <summary>Quand le remboursement a été DÉCIDÉ (l'argent n'était pas encore parti).</summary>
    public DateTime? RefundApprovedAtUtc { get; private set; }

    /// <summary>Quand l'argent a réellement été VERSÉ.</summary>
    public DateTime? RefundExecutedAtUtc { get; private set; }

    /// <summary>
    /// Référence du versement chez FedaPay. C'est la PREUVE que l'argent est parti.
    ///
    /// Le jour où un client affirme n'avoir rien reçu, c'est cette référence qui
    /// permet de retrouver l'opération et de trancher. Un statut « Remboursé » sans
    /// référence ne prouve rien du tout — c'est exactement l'état d'où l'on vient.
    /// </summary>
    public string? RefundReference { get; private set; }

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

    /// <summary>
    /// ÉTAPE 1 — Le vendeur (ou l'admin) valide le remboursement et son montant.
    ///
    /// ⚠️ AUCUN ARGENT NE BOUGE ICI, et c'est délibéré.
    ///
    /// Cette méthode s'appelait <c>Refund()</c> et passait directement à
    /// « Remboursé ». Elle mentait : rien n'appelait la banque. Le client n'était
    /// jamais payé, mais la base affirmait le contraire.
    ///
    /// Elle ne fait plus qu'une chose — enregistrer une DÉCISION. Le versement,
    /// lui, est confirmé par <see cref="ConfirmRefundExecuted"/>, et seulement une
    /// fois l'argent réellement parti.
    /// </summary>
    public Result ApproveRefund(decimal amount)
    {
        if (Status != ReturnStatus.Received)
        {
            return Result.Failure(Error.Conflict("returns.not_received", "Le retour doit être réceptionné avant remboursement."));
        }

        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation("returns.amount_invalid", "Le montant remboursé doit être positif."));
        }

        Status = ReturnStatus.RefundPending;
        RefundAmount = amount;
        RefundId = Guid.NewGuid();
        RefundApprovedAtUtc = DateTime.UtcNow;

        Raise(new ReturnRefundApprovedDomainEvent(Id.Value, OrderId, BuyerId, SellerId, amount, Currency));
        return Result.Success();
    }

    /// <summary>
    /// ÉTAPE 2 — L'argent est PARTI. Un administrateur l'a versé depuis le tableau
    /// de bord FedaPay et en apporte la référence.
    ///
    /// ⚠️ C'EST LA SEULE PORTE VERS « REMBOURSÉ », et elle exige une preuve.
    ///
    /// La référence n'est pas décorative : c'est elle qui permet, le jour où le
    /// client affirme n'avoir rien reçu, de retrouver l'opération chez FedaPay et
    /// de trancher. Sans elle, votre parole contre la sienne.
    ///
    /// C'est aussi ici — et ici seulement — que l'on émet l'événement qui
    /// contre-passe les gains du vendeur. Le déclencher à la validation aurait
    /// débité un vendeur pour un versement qui pouvait encore échouer.
    /// </summary>
    public Result ConfirmRefundExecuted(string providerReference, DateTime nowUtc)
    {
        if (Status != ReturnStatus.RefundPending)
        {
            return Result.Failure(Error.Conflict(
                "returns.refund_not_pending",
                "Seul un remboursement validé et en attente de versement peut être confirmé."));
        }

        if (string.IsNullOrWhiteSpace(providerReference))
        {
            return Result.Failure(Error.Validation(
                "returns.reference_required",
                "La référence du versement (FedaPay) est obligatoire : elle prouve que l'argent est parti."));
        }

        Status = ReturnStatus.Refunded;
        RefundReference = providerReference.Trim();
        RefundExecutedAtUtc = nowUtc;
        ResolvedAtUtc = nowUtc;

        Raise(new ReturnRefundedDomainEvent(
            Id.Value, OrderId, BuyerId, SellerId, RefundAmount!.Value, Currency, RefundReference));

        return Result.Success();
    }
}
