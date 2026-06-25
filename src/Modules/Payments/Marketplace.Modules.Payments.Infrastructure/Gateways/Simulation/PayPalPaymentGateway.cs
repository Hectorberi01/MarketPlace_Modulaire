using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;

/// <summary>
/// Adaptateur PayPal (stub sandbox). Mappe les types d'événements PayPal
/// (<c>event_type</c>) vers les résultats normalisés et lit la référence sous
/// <c>resource.id</c>. Pour passer en réel : injecter le SDK PayPal (Orders v2)
/// et vérifier les webhooks via l'API de vérification de signature.
/// </summary>
public sealed class PayPalPaymentGateway : SimulatedPaymentGateway
{
    private readonly PayPalOptions _options;

    public PayPalPaymentGateway(PayPalOptions options) => _options = options;

    public override string Provider => "PayPal";

    protected override string CheckoutPrefix => "PAYID";
    protected override string IntentPrefix => "ORDER";
    protected override string CheckoutBaseUrl => _options.CheckoutBaseUrl;
    protected override string WebhookSecret => _options.WebhookSecret;
    protected override string EventTypeField => "event_type";

    protected override GatewayOutcome MapOutcome(string eventType) => eventType switch
    {
        "PAYMENT.CAPTURE.COMPLETED" => GatewayOutcome.Captured,
        "CHECKOUT.ORDER.APPROVED" => GatewayOutcome.Captured,
        "PAYMENT.CAPTURE.DENIED" => GatewayOutcome.Failed,
        "PAYMENT.CAPTURE.DECLINED" => GatewayOutcome.Failed,
        "PAYMENT.CAPTURE.REFUNDED" => GatewayOutcome.Refunded,
        _ => GatewayOutcome.Ignored
    };

    protected override string? ExtractReference(JsonElement root)
    {
        // Payload de test : { "event_type": "...", "providerReference": "ORDER_..." }
        if (root.TryGetProperty("providerReference", out var direct))
        {
            return direct.GetString();
        }

        // Payload natif PayPal : { "resource": { "id": "..." } }
        if (root.TryGetProperty("resource", out var resource)
            && resource.TryGetProperty("id", out var id))
        {
            return id.GetString();
        }

        return null;
    }
}
