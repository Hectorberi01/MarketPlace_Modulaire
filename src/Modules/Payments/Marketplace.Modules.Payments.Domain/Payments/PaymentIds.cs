namespace Marketplace.Modules.Payments.Domain.Payments;

/// <summary>Identité forte d'un paiement.</summary>
public readonly record struct PaymentId(Guid Value)
{
    public static PaymentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Moyen de paiement (Mobile Money en priorité sur le marché visé).</summary>
public enum PaymentMethod
{
    MobileMoney = 0,
    Card = 1,
    BankTransfer = 2,
    CashOnDelivery = 3
}

/// <summary>
/// Mode d'interaction avec le prestataire (PSP) :
/// - HostedCheckout : page de paiement hébergée par le PSP (redirection).
/// - PaymentIntent : intention créée côté serveur, confirmée côté client (SDK).
/// </summary>
public enum PaymentFlow
{
    HostedCheckout = 0,
    PaymentIntent = 1
}

/// <summary>Statut du paiement, piloté par le Saga de commande.</summary>
public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Captured = 2,
    Failed = 3,
    Refunded = 4
}
