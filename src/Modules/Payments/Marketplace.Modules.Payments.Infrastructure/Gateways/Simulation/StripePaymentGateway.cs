using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;

/// <summary>
/// Adaptateur Stripe (stub sandbox). Mappe les types d'événements Stripe vers
/// les résultats normalisés et lit la référence sous <c>data.object.id</c>.
/// Pour passer en réel : injecter Stripe.net, utiliser SessionService /
/// PaymentIntentService et EventUtility.ConstructEvent pour la signature.
/// </summary>
public sealed class StripePaymentGateway : SimulatedPaymentGateway
{
    private readonly StripeOptions _options;

    public StripePaymentGateway(StripeOptions options) => _options = options;

    public override string Provider => "Stripe";

    protected override string CheckoutPrefix => "cs";
    protected override string IntentPrefix => "pi";
    protected override string CheckoutBaseUrl => _options.CheckoutBaseUrl;
    protected override string WebhookSecret => _options.WebhookSecret;
    protected override string EventTypeField => "type";

    protected override GatewayOutcome MapOutcome(string eventType) => eventType switch
    {
        "checkout.session.completed" => GatewayOutcome.Captured,
        "checkout.session.async_payment_succeeded" => GatewayOutcome.Captured,
        "payment_intent.succeeded" => GatewayOutcome.Captured,
        "payment_intent.payment_failed" => GatewayOutcome.Failed,
        "checkout.session.expired" => GatewayOutcome.Failed,
        "charge.refunded" => GatewayOutcome.Refunded,
        _ => GatewayOutcome.Ignored
    };

    protected override string? ExtractReference(JsonElement root)
    {
        // Payload de test : { "type": "...", "providerReference": "cs_..." }
        if (root.TryGetProperty("providerReference", out var direct))
        {
            return direct.GetString();
        }

        // Payload natif Stripe : { "data": { "object": { "id": "cs_..." } } }
        if (root.TryGetProperty("data", out var data)
            && data.TryGetProperty("object", out var obj)
            && obj.TryGetProperty("id", out var id))
        {
            return id.GetString();
        }

        return null;
    }
}
