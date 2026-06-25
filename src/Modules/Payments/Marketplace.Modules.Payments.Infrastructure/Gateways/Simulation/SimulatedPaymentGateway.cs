using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;

/// <summary>
/// Base commune des adaptateurs PSP en mode « stub sandbox » : crée des
/// sessions/intentions simulées, vérifie la signature des webhooks par HMAC, et
/// normalise les payloads. Les appels réseau réels (SDK Stripe / PayPal) viennent
/// remplacer les méthodes marquées TODO, sans changer le port consommé par le métier.
/// </summary>
public abstract class SimulatedPaymentGateway : IPaymentGateway
{
    private static readonly JsonDocumentOptions JsonOptions = new() { AllowTrailingCommas = true };

    public abstract string Provider { get; }

    /// <summary>Par défaut, un PSP carte/portefeuille n'exige pas le numéro du payeur.</summary>
    public virtual bool RequiresPayerPhone => false;

    /// <summary>Préfixe d'identifiant (ex. « cs » pour Checkout Session, « pi » pour PaymentIntent).</summary>
    protected abstract string CheckoutPrefix { get; }
    protected abstract string IntentPrefix { get; }
    protected abstract string CheckoutBaseUrl { get; }

    /// <summary>
    /// Secret de signature des webhooks ; vide = sandbox permissif (signature non exigée).
    /// </summary>
    protected abstract string WebhookSecret { get; }

    /// <summary>Nom du champ portant le type d'événement dans le payload du PSP.</summary>
    protected abstract string EventTypeField { get; }

    /// <summary>Mappe un type d'événement PSP vers un résultat normalisé.</summary>
    protected abstract GatewayOutcome MapOutcome(string eventType);

    public virtual Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken cancellationToken = default)
    {
        // TODO: remplacer par Stripe Checkout Sessions / PayPal Orders (création réelle).
        var reference = $"{CheckoutPrefix}_{Guid.NewGuid():N}";
        var redirectUrl = $"{CheckoutBaseUrl}/{reference}?return={Uri.EscapeDataString(context.ReturnUrl ?? string.Empty)}";
        return Task.FromResult(new GatewaySession(reference, redirectUrl, ClientSecret: null));
    }

    public virtual Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken cancellationToken = default)
    {
        // TODO: remplacer par Stripe PaymentIntents / PayPal Orders (intent=AUTHORIZE/CAPTURE).
        var reference = $"{IntentPrefix}_{Guid.NewGuid():N}";
        var clientSecret = $"{reference}_secret_{Guid.NewGuid():N}";
        return Task.FromResult(new GatewaySession(reference, RedirectUrl: null, clientSecret));
    }

    public Task<GatewayEvent> ParseWebhookAsync(string rawBody, string? signatureHeader, CancellationToken cancellationToken = default)
    {
        if (!VerifySignature(rawBody, signatureHeader))
        {
            return Task.FromResult(new GatewayEvent(Verified: false, GatewayOutcome.Ignored, null, null));
        }

        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody, JsonOptions);
            var root = document.RootElement;

            var eventType = root.TryGetProperty(EventTypeField, out var typeEl) ? typeEl.GetString() ?? string.Empty : string.Empty;
            var reference = ExtractReference(root);
            var outcome = MapOutcome(eventType);

            return Task.FromResult(new GatewayEvent(Verified: true, outcome, reference, FailureReason: outcome == GatewayOutcome.Failed ? eventType : null));
        }
        catch (JsonException)
        {
            return Task.FromResult(new GatewayEvent(Verified: false, GatewayOutcome.Ignored, null, "Payload JSON invalide."));
        }
    }

    public virtual Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default)
        // TODO: interroger réellement le PSP (Stripe Sessions.Get / PayPal Orders.Get).
        // En sandbox, on considère la session aboutie au retour de redirection.
        => Task.FromResult(new GatewayEvent(Verified: true, GatewayOutcome.Captured, providerReference, null));

    public Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken cancellationToken = default)
        // TODO: Stripe Refunds.Create / PayPal Captures.Refund.
        => Task.FromResult(new GatewayRefundResult(Success: true, providerReference, Error: null));

    /// <summary>
    /// Extrait l'identifiant de corrélation du payload. Cherche d'abord un champ
    /// « providerReference » de premier niveau (payload de test), sinon délègue à
    /// l'adaptateur concret (emplacement natif du PSP).
    /// </summary>
    protected virtual string? ExtractReference(JsonElement root)
        => root.TryGetProperty("providerReference", out var refEl) ? refEl.GetString() : null;

    /// <summary>
    /// Vérifie la signature HMAC-SHA256 du corps brut. Secret vide = sandbox
    /// permissif (on accepte sans signature pour faciliter les tests locaux).
    /// </summary>
    protected bool VerifySignature(string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(WebhookSecret))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        var expected = ComputeSignature(rawBody, WebhookSecret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signatureHeader.Trim()));
    }

    /// <summary>Signature HMAC-SHA256 en hexadécimal (schéma de test, proche de Stripe).</summary>
    public static string ComputeSignature(string rawBody, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
