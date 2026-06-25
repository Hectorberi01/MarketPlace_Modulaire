namespace Marketplace.Modules.Payments.Application.Abstractions.Gateways;

/// <summary>
/// Contexte d'une demande de paiement transmis au PSP. <see cref="PayerMsisdn"/>
/// (numéro du payeur) n'est renseigné que pour le Mobile Money (RequestToPay).
/// </summary>
public sealed record GatewayChargeContext(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string? ReturnUrl,
    string? CancelUrl,
    string? PayerMsisdn = null);

/// <summary>
/// Session créée côté PSP. Selon le flux :
/// - HostedCheckout : <see cref="RedirectUrl"/> renseigné (rediriger l'acheteur).
/// - PaymentIntent : <see cref="ClientSecret"/> renseigné (confirmer côté client).
/// <see cref="ProviderReference"/> est l'id de corrélation (session / intent).
/// </summary>
public sealed record GatewaySession(string ProviderReference, string? RedirectUrl, string? ClientSecret);

/// <summary>Résultat normalisé d'un événement PSP (webhook ou interrogation de statut).</summary>
public enum GatewayOutcome
{
    Pending = 0,
    Captured = 1,
    Failed = 2,
    Refunded = 3,
    Ignored = 4
}

/// <summary>Événement PSP normalisé, indépendant du fournisseur.</summary>
public sealed record GatewayEvent(bool Verified, GatewayOutcome Outcome, string? ProviderReference, string? FailureReason);

/// <summary>Résultat d'un remboursement demandé au PSP.</summary>
public sealed record GatewayRefundResult(bool Success, string? ProviderReference, string? Error);

/// <summary>
/// Port (hexagonal) d'un prestataire de paiement. Une implémentation par PSP
/// (Stripe, PayPal…) vit dans Infrastructure ; le métier ne dépend que de ce
/// contrat. Couvre les deux flux (checkout hébergé / intention serveur) et les
/// deux modes de confirmation (webhook signé / retour de redirection).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Nom du prestataire, ex. « Stripe », « PayPal », « MtnMomo », « Moov » (insensible à la casse).</summary>
    string Provider { get; }

    /// <summary>Vrai si le PSP exige le numéro du payeur (Mobile Money : RequestToPay).</summary>
    bool RequiresPayerPhone { get; }

    /// <summary>Crée une page de paiement hébergée et renvoie l'URL de redirection.</summary>
    Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken cancellationToken = default);

    /// <summary>Crée une intention de paiement à confirmer côté client (renvoie un client secret).</summary>
    Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken cancellationToken = default);

    /// <summary>Vérifie la signature et normalise le payload d'un webhook PSP.</summary>
    Task<GatewayEvent> ParseWebhookAsync(string rawBody, string? signatureHeader, CancellationToken cancellationToken = default);

    /// <summary>Interroge le statut courant d'une session/intention (retour de redirection, réconciliation).</summary>
    Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default);

    /// <summary>Demande le remboursement d'un paiement encaissé.</summary>
    Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken cancellationToken = default);
}
