using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Real;

/// <summary>
/// Adaptateur Stripe RÉEL (API REST). Authentification par clé secrète (Bearer),
/// Checkout Session (redirection) ou PaymentIntent (client secret), statut par
/// GET, et vérification de webhook selon le schéma natif Stripe
/// (en-tête « Stripe-Signature : t=…,v1=… »).
///
/// Renseigne « Payments:Stripe:ApiKey » (clé secrète sk_…) pour activer cet
/// adaptateur à la place du stub.
/// </summary>
public sealed class StripeHttpGateway : HttpPaymentGatewayBase
{
    // Devises « zéro décimale » : le montant est l'entier tel quel (pas ×100).
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "XOF", "XAF", "XPF", "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV"
    };

    private readonly StripeOptions _options;

    public StripeHttpGateway(IHttpClientFactory httpClientFactory, StripeOptions options)
        : base(httpClientFactory) => _options = options;

    public const string ClientName = "stripe";

    public override string Provider => "Stripe";

    protected override string HttpClientName => ClientName;
    protected override string WebhookSecret => _options.WebhookSecret;
    protected override string EventTypeField => "type";

    public override async Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["mode"] = "payment",
            ["success_url"] = context.ReturnUrl ?? _options.SuccessUrl,
            ["cancel_url"] = context.CancelUrl ?? _options.CancelUrl,
            ["client_reference_id"] = context.OrderId.ToString(),
            ["line_items[0][quantity]"] = "1",
            ["line_items[0][price_data][currency]"] = context.Currency.ToLowerInvariant(),
            ["line_items[0][price_data][unit_amount]"] = FormatAmount(context.Amount, context.Currency),
            ["line_items[0][price_data][product_data][name]"] = $"Commande {context.OrderId}",
            ["payment_intent_data[metadata][orderId]"] = context.OrderId.ToString()
        };

        var session = await PostFormAsync<StripeObject>("v1/checkout/sessions", form, ct);
        return new GatewaySession(session?.id ?? string.Empty, session?.url, ClientSecret: null);
    }

    public override async Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["amount"] = FormatAmount(context.Amount, context.Currency),
            ["currency"] = context.Currency.ToLowerInvariant(),
            ["automatic_payment_methods[enabled]"] = "true",
            ["metadata[orderId]"] = context.OrderId.ToString()
        };

        var intent = await PostFormAsync<StripeObject>("v1/payment_intents", form, ct);
        return new GatewaySession(intent?.id ?? string.Empty, RedirectUrl: null, intent?.client_secret);
    }

    public override async Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken ct = default)
    {
        // La référence est soit une session Checkout (cs_…), soit un PaymentIntent (pi_…).
        var path = providerReference.StartsWith("cs_", StringComparison.Ordinal)
            ? $"v1/checkout/sessions/{providerReference}"
            : $"v1/payment_intents/{providerReference}";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        Authorize(request);

        var response = await CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var obj = await response.Content.ReadFromJsonAsync<StripeObject>(ct);
        // payment_status pour une session ; status pour un intent.
        var outcome = MapStatus(obj?.payment_status ?? obj?.status ?? string.Empty);
        return new GatewayEvent(Verified: true, outcome, providerReference, null);
    }

    public override async Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["payment_intent"] = providerReference };
        try
        {
            var refund = await PostFormAsync<StripeObject>("v1/refunds", form, ct);
            return new GatewayRefundResult(Success: true, refund?.id, Error: null);
        }
        catch (HttpRequestException ex)
        {
            return new GatewayRefundResult(Success: false, providerReference, ex.Message);
        }
    }

    /// <summary>Vérifie l'en-tête « Stripe-Signature » (t=…,v1=…) puis normalise l'événement.</summary>
    public override Task<GatewayEvent> ParseWebhookAsync(string rawBody, string? signatureHeader, CancellationToken cancellationToken = default)
    {
        if (!VerifyStripeSignature(rawBody, signatureHeader))
        {
            return Task.FromResult(new GatewayEvent(Verified: false, GatewayOutcome.Ignored, null, null));
        }

        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            var reference = ExtractReference(root);
            var outcome = MapOutcome(type);
            return Task.FromResult(new GatewayEvent(Verified: true, outcome, reference, outcome == GatewayOutcome.Failed ? type : null));
        }
        catch (JsonException)
        {
            return Task.FromResult(new GatewayEvent(Verified: false, GatewayOutcome.Ignored, null, "Payload JSON invalide."));
        }
    }

    private bool VerifyStripeSignature(string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret))
        {
            return true; // sandbox permissif
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        string? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in signatureHeader.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            if (kv[0].Trim() == "t")
            {
                timestamp = kv[1].Trim();
            }
            else if (kv[0].Trim() == "v1")
            {
                signatures.Add(kv[1].Trim());
            }
        }

        if (timestamp is null || signatures.Count == 0)
        {
            return false;
        }

        var signedPayload = $"{timestamp}.{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.WebhookSecret));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();

        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(s)));
    }

    protected override GatewayOutcome MapOutcome(string eventType) => eventType switch
    {
        "checkout.session.completed" or "checkout.session.async_payment_succeeded" or "payment_intent.succeeded" => GatewayOutcome.Captured,
        "payment_intent.payment_failed" or "checkout.session.expired" or "checkout.session.async_payment_failed" => GatewayOutcome.Failed,
        "charge.refunded" => GatewayOutcome.Refunded,
        _ => GatewayOutcome.Ignored
    };

    private static GatewayOutcome MapStatus(string status) => status.ToLowerInvariant() switch
    {
        "paid" or "succeeded" => GatewayOutcome.Captured,
        "canceled" or "expired" => GatewayOutcome.Failed,
        _ => GatewayOutcome.Pending
    };

    protected override string? ExtractReference(JsonElement root)
        => root.TryGetProperty("data", out var data)
           && data.TryGetProperty("object", out var obj)
           && obj.TryGetProperty("id", out var id)
            ? id.GetString()
            : null;

    private async Task<T?> PostFormAsync<T>(string path, IDictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) };
        Authorize(request);

        var response = await CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }

    private void Authorize(HttpRequestMessage request)
        => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

    private static string FormatAmount(decimal amount, string currency)
    {
        var minor = ZeroDecimalCurrencies.Contains(currency) ? Math.Round(amount) : Math.Round(amount * 100m);
        return ((long)minor).ToString(CultureInfo.InvariantCulture);
    }

    private sealed record StripeObject(string? id, string? url, string? client_secret, string? status, string? payment_status);
}
