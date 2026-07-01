using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;

/// <summary>
/// Stub sandbox FedaPay : simule la page de paiement hébergée (renvoie une URL de
/// redirection factice) sans toucher le réseau. Permet de faire tourner le parcours
/// complet sans clé FedaPay. Dès qu'une clé est configurée, l'installer bascule sur
/// <see cref="Real.FedaPayHttpGateway"/>.
/// </summary>
public sealed class FedaPayPaymentGateway : SimulatedPaymentGateway
{
    private readonly FedaPayOptions _options;

    public FedaPayPaymentGateway(FedaPayOptions options) => _options = options;

    public override string Provider => "FedaPay";

    protected override string CheckoutPrefix => "feda_cs";
    protected override string IntentPrefix => "feda_pi";
    protected override string CheckoutBaseUrl => "https://sandbox-checkout.fedapay.com/pay";
    protected override string WebhookSecret => _options.WebhookSecret;
    protected override string EventTypeField => "status";

    protected override GatewayOutcome MapOutcome(string eventType) => eventType.ToLowerInvariant() switch
    {
        "approved" or "transferred" => GatewayOutcome.Captured,
        "declined" or "canceled" or "cancelled" => GatewayOutcome.Failed,
        "pending" => GatewayOutcome.Pending,
        "refunded" => GatewayOutcome.Refunded,
        _ => GatewayOutcome.Ignored
    };
}
