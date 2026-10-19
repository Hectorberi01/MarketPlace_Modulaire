using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Real;

/// <summary>
/// Adaptateur PayPal RÉEL (Orders v2). Authentification OAuth client_credentials
/// (Basic ClientId:Secret), création d'une commande (redirection vers le lien
/// « approve »), puis capture au retour. PayPal étant basé sur la redirection,
/// les deux flux logiques (checkout / intent) renvoient une URL d'approbation.
///
/// Renseigne « Payments:PayPal:ClientId » et « Secret » pour activer cet
/// adaptateur à la place du stub.
///
/// Note webhook : la vérification réelle PayPal passe par l'API
/// verify-webhook-signature (en-têtes de transmission + WebhookId). Ici on
/// s'appuie sur le secret HMAC partagé (à durcir avant production).
/// </summary>
public sealed class PayPalHttpGateway : HttpPaymentGatewayBase
{
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTime _tokenExpiresAtUtc = DateTime.MinValue;

    public PayPalHttpGateway(IHttpClientFactory httpClientFactory, PayPalOptions options)
        : base(httpClientFactory) => _options = options;

    public const string ClientName = "paypal";

    public override string Provider => "PayPal";

    protected override string HttpClientName => ClientName;
    protected override string WebhookSecret => _options.WebhookSecret;
    protected override string BaseUrl => _options.BaseUrl;
    protected override string EventTypeField => "event_type";

    public override Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken ct = default)
        => CreateOrderAsync(context, ct);

    public override Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken ct = default)
        => CreateOrderAsync(context, ct);

    private async Task<GatewaySession> CreateOrderAsync(GatewayChargeContext context, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "v2/checkout/orders")
        {
            Content = JsonContent.Create(new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                    new
                    {
                        custom_id = context.OrderId.ToString(),
                        amount = new
                        {
                            currency_code = context.Currency,
                            value = FormatAmount(context.Amount, context.Currency)
                        }
                    }
                },
                application_context = new
                {
                    return_url = context.ReturnUrl ?? _options.ReturnUrl,
                    cancel_url = context.CancelUrl ?? _options.CancelUrl
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var order = await response.Content.ReadFromJsonAsync<PayPalOrder>(ct);
        var approveUrl = order?.links?.FirstOrDefault(l => string.Equals(l.rel, "approve", StringComparison.OrdinalIgnoreCase))?.href;
        return new GatewaySession(order?.id ?? string.Empty, approveUrl, ClientSecret: null);
    }

    public override async Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);

        using var get = new HttpRequestMessage(HttpMethod.Get, $"v2/checkout/orders/{providerReference}");
        get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResponse = await CreateClient().SendAsync(get, ct);
        getResponse.EnsureSuccessStatusCode();
        var order = await getResponse.Content.ReadFromJsonAsync<PayPalOrder>(ct);

        // Au retour de redirection, l'acheteur a approuvé : on capture pour encaisser.
        if (string.Equals(order?.status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            using var capture = new HttpRequestMessage(HttpMethod.Post, $"v2/checkout/orders/{providerReference}/capture")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            capture.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var captureResponse = await CreateClient().SendAsync(capture, ct);
            captureResponse.EnsureSuccessStatusCode();
            var captured = await captureResponse.Content.ReadFromJsonAsync<PayPalOrder>(ct);
            return new GatewayEvent(Verified: true, MapStatus(captured?.status ?? string.Empty), providerReference, null);
        }

        return new GatewayEvent(Verified: true, MapStatus(order?.status ?? string.Empty), providerReference, null);
    }

    public override Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken ct = default)
        // Le remboursement PayPal cible la capture (v2/payments/captures/{id}/refund),
        // dont l'id diffère de l'order : à brancher avec le suivi des captures.
        => Task.FromResult(new GatewayRefundResult(Success: false, providerReference, "Remboursement PayPal non pris en charge pour l'instant."));

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresAtUtc)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresAtUtc)
            {
                return _cachedToken;
            }

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.Secret}"));

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await CreateClient().SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(ct)
                ?? throw new InvalidOperationException("Réponse de jeton PayPal vide.");

            _cachedToken = token.access_token;
            _tokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(0, token.expires_in - 60));
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    protected override GatewayOutcome MapOutcome(string eventType) => eventType switch
    {
        "PAYMENT.CAPTURE.COMPLETED" or "CHECKOUT.ORDER.APPROVED" => GatewayOutcome.Captured,
        "PAYMENT.CAPTURE.DENIED" or "PAYMENT.CAPTURE.DECLINED" => GatewayOutcome.Failed,
        "PAYMENT.CAPTURE.REFUNDED" => GatewayOutcome.Refunded,
        _ => GatewayOutcome.Ignored
    };

    private static GatewayOutcome MapStatus(string status) => status.ToUpperInvariant() switch
    {
        "COMPLETED" or "APPROVED" => GatewayOutcome.Captured,
        "VOIDED" or "DECLINED" => GatewayOutcome.Failed,
        _ => GatewayOutcome.Pending
    };

    protected override string? ExtractReference(JsonElement root)
        => root.TryGetProperty("resource", out var resource) && resource.TryGetProperty("id", out var id)
            ? id.GetString()
            : null;

    /// <summary>
    /// Montant au format PayPal (chaîne) : entier pour une devise sans subdivision,
    /// deux décimales sinon. La liste des devises « zéro décimale » était recopiée en
    /// tête de ce fichier ; elle vit maintenant dans <see cref="Money"/>, seule
    /// détentrice de la précision de chaque devise.
    /// </summary>
    private static string FormatAmount(decimal amount, string currency)
        => Money.Round(amount, currency)
            .ToString("F" + Money.DecimalsFor(currency), CultureInfo.InvariantCulture);

    private sealed record AccessTokenResponse(string access_token, string token_type, int expires_in);

    private sealed record PayPalOrder(string? id, string? status, PayPalLink[]? links);

    private sealed record PayPalLink(string? href, string? rel, string? method);
}
