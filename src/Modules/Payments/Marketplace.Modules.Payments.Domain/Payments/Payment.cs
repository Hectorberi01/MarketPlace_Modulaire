using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Payments.Domain.Payments.Events;

namespace Marketplace.Modules.Payments.Domain.Payments;

/// <summary>
/// Paiement d'une commande. Modélise le cycle PSP (Mobile Money, carte…) :
/// initiation → autorisation → encaissement, ou échec / remboursement. Émet les
/// faits que le Saga d'Ordering consomme pour confirmer ou annuler la commande.
/// </summary>
public sealed class Payment : AggregateRoot<PaymentId>
{
    private Payment()
    {
    }

    private Payment(PaymentId id, Guid orderId, Guid buyerId, Money amount, PaymentMethod method, string provider, PaymentFlow flow)
        : base(id)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Method = method;
        Provider = provider;
        Flow = flow;
        Status = PaymentStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;

        Raise(new PaymentInitiatedDomainEvent(id.Value, orderId, amount.Amount, amount.Currency));
    }

    public Guid OrderId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Money Amount { get; private set; } = default!;
    public PaymentMethod Method { get; private set; }
    public string Provider { get; private set; } = default!;
    public PaymentFlow Flow { get; private set; }
    public string? ProviderReference { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CapturedAtUtc { get; private set; }

    /// <summary>
    /// Date de libération de l'escrow. Tant qu'elle est nulle après encaissement,
    /// les fonds sont gelés (modèle marketplace : on ne reverse au vendeur qu'à la
    /// livraison confirmée).
    /// </summary>
    public DateTime? EscrowReleasedAt { get; private set; }

    /// <summary>Vrai si le paiement est encaissé mais l'escrow pas encore libéré.</summary>
    public bool IsEscrowHeld => Status == PaymentStatus.Captured && EscrowReleasedAt is null;

    public static Result<Payment> Create(Guid orderId, Guid buyerId, Money amount, PaymentMethod method, string provider, PaymentFlow flow)
    {
        if (orderId == Guid.Empty)
        {
            return Error.Validation("payments.order_required", "La commande est obligatoire.");
        }

        if (amount.Amount <= 0m)
        {
            return Error.Validation("payments.amount_invalid", "Le montant à payer doit être positif.");
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            return Error.Validation("payments.provider_required", "Le prestataire de paiement est obligatoire.");
        }

        return new Payment(PaymentId.New(), orderId, buyerId, amount, method, provider.Trim(), flow);
    }

    /// <summary>
    /// Rattache la référence de session/intention renvoyée par le PSP au moment
    /// de l'initiation (session_id du Checkout ou id du PaymentIntent). Sert de
    /// corrélation pour les webhooks et le retour de redirection.
    /// </summary>
    public Result AttachGatewaySession(string providerReference)
    {
        if (Status != PaymentStatus.Pending)
        {
            return Result.Failure(Error.Conflict("payments.invalid_transition", "La session PSP ne peut être rattachée qu'à un paiement en attente."));
        }

        if (string.IsNullOrWhiteSpace(providerReference))
        {
            return Result.Failure(Error.Validation("payments.provider_reference_required", "La référence PSP est obligatoire."));
        }

        ProviderReference = providerReference.Trim();
        return Result.Success();
    }

    /// <summary>Autorisation préalable (réservation de fonds), sans encaissement.</summary>
    public Result Authorize(string providerReference)
    {
        if (Status != PaymentStatus.Pending)
        {
            return Result.Failure(Error.Conflict("payments.invalid_transition", "Le paiement n'est pas en attente d'autorisation."));
        }

        Status = PaymentStatus.Authorized;
        ProviderReference = providerReference;
        return Result.Success();
    }

    /// <summary>Encaissement effectif. Émet PaymentCaptured.</summary>
    public Result Capture(string providerReference)
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Authorized))
        {
            return Result.Failure(Error.Conflict("payments.invalid_transition", "Le paiement ne peut pas être encaissé dans cet état."));
        }

        Status = PaymentStatus.Captured;
        ProviderReference = providerReference;
        CapturedAtUtc = DateTime.UtcNow;
        Raise(new PaymentCapturedDomainEvent(Id.Value, OrderId));
        return Result.Success();
    }

    /// <summary>Échec du paiement. Émet PaymentFailed.</summary>
    public Result Fail(string reason)
    {
        if (Status is PaymentStatus.Captured or PaymentStatus.Refunded)
        {
            return Result.Failure(Error.Conflict("payments.invalid_transition", "Un paiement encaissé ne peut pas échouer."));
        }

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        Raise(new PaymentFailedDomainEvent(Id.Value, OrderId, reason));
        return Result.Success();
    }

    /// <summary>Remboursement d'un paiement encaissé. Émet PaymentRefunded.</summary>
    public Result Refund()
    {
        if (Status != PaymentStatus.Captured)
        {
            return Result.Failure(Error.Conflict("payments.not_refundable", "Seul un paiement encaissé peut être remboursé."));
        }

        Status = PaymentStatus.Refunded;
        Raise(new PaymentRefundedDomainEvent(Id.Value, OrderId));
        return Result.Success();
    }

    /// <summary>
    /// Libère l'escrow à la livraison confirmée : les fonds encaissés deviennent
    /// reversables au vendeur (le payout est ensuite produit par Settlement).
    /// Idempotent : sans effet si déjà libéré.
    /// </summary>
    public Result ReleaseEscrow()
    {
        if (Status != PaymentStatus.Captured)
        {
            return Result.Failure(Error.Conflict("payments.escrow_not_releasable", "Seul un paiement encaissé peut voir son escrow libéré."));
        }

        if (EscrowReleasedAt is not null)
        {
            return Result.Success();
        }

        EscrowReleasedAt = DateTime.UtcNow;
        return Result.Success();
    }
}
