using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Payments.Application.Payments.Commands.InitiatePayment;

/// <summary>
/// Initie un paiement pour une commande en attente. <paramref name="Provider"/>
/// = « Stripe » / « PayPal ». <paramref name="Flow"/> = « HostedCheckout »
/// (redirection) ou « PaymentIntent » (confirmation côté client).
/// </summary>
public sealed record InitiatePaymentCommand(
    Guid OrderId,
    string Method,
    string Provider,
    string Flow = "HostedCheckout",
    string? ReturnUrl = null,
    string? CancelUrl = null,
    string? PayerPhone = null) : ICommand<InitiatePaymentResult>;

/// <summary>
/// Résultat d'initiation : selon le flux, <see cref="RedirectUrl"/> (checkout
/// hébergé) ou <see cref="ClientSecret"/> (intention) est renseigné.
/// </summary>
public sealed record InitiatePaymentResult(
    Guid PaymentId,
    string Provider,
    string Flow,
    string ProviderReference,
    string? RedirectUrl,
    string? ClientSecret);
