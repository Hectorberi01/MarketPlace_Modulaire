using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Real;

/// <summary>
/// Adaptateur Moov Money RÉEL. Flux aligné sur MoMo : jeton OAuth
/// (client_credentials, Basic MerchantId:ApiKey), puis création d'un paiement
/// (RequestToPay) renvoyant une référence de transaction, et lecture du statut.
///
/// Le contrat Moov varie selon le pays / l'agrégateur : les chemins
/// (TokenPath, PaymentPath) et la forme du payload sont configurables et à
/// confirmer avec ta documentation Moov. Renseigne « Payments:Moov » pour
/// remplacer le stub par cet adaptateur.
/// </summary>
public sealed class MoovHttpGateway : HttpPaymentGatewayBase
{
    private readonly MoovOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTime _tokenExpiresAtUtc = DateTime.MinValue;

    public MoovHttpGateway(IHttpClientFactory httpClientFactory, MoovOptions options)
        : base(httpClientFactory) => _options = options;

    public const string ClientName = "moov-money";

    public override string Provider => "Moov";
    public override bool RequiresPayerPhone => true;

    protected override string HttpClientName => ClientName;
    protected override string WebhookSecret => _options.WebhookSecret;
    protected override string BaseUrl => _options.BaseUrl;
    protected override string EventTypeField => "status";

    public override Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken ct = default)
        => RequestToPayAsync(context, ct);

    public override Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken ct = default)
        => RequestToPayAsync(context, ct);

    private async Task<GatewaySession> RequestToPayAsync(GatewayChargeContext context, CancellationToken ct)
    {
        // LA DEVISE VIENT DE LA COMMANDE, ET ELLE DOIT ÊTRE CELLE DU COMPTE MARCHAND.
        // Le payload envoyait `_options.Currency` avec `context.Amount` : l'étiquette
        // de la configuration collée sur le montant de la commande. Voir l'encadré de
        // `EnsureCurrencyIsSupported`.
        //
        // Contrôle fait AVANT le jeton : un refus ne doit rien démarrer côté Moov.
        var currency = EnsureCurrencyIsSupported(Provider, context.Currency, _options.Currency);

        var token = await GetAccessTokenAsync(ct);
        var externalReference = $"MOOV-{Guid.NewGuid():N}";

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.PaymentPath)
        {
            Content = JsonContent.Create(new
            {
                // « amount » est une CHAÎNE chez Moov : formatée à la précision de la devise
                // (« 5000 » en XOF, « 50.00 » en EUR) au lieu d'être tronquée à l'entier.
                amount = FormatAmountForCurrency(context.Amount, currency),
                currency,
                msisdn = NormalizeMsisdn(context.PayerMsisdn),
                externalReference,
                description = $"Commande {context.OrderId}",
                callbackUrl = string.IsNullOrWhiteSpace(_options.CallbackUrl) ? null : _options.CallbackUrl
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        // On privilégie l'id de transaction renvoyé par Moov ; à défaut, notre référence.
        var payment = await response.Content.ReadFromJsonAsync<MoovPaymentResponse>(ct);
        var reference = payment?.transactionId ?? payment?.reference ?? externalReference;

        return new GatewaySession(reference, RedirectUrl: null, ClientSecret: null);
    }

    public override async Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.PaymentPath}/{providerReference}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var payment = await response.Content.ReadFromJsonAsync<MoovPaymentResponse>(ct);
        var outcome = MapOutcome(payment?.status ?? string.Empty);
        return new GatewayEvent(Verified: true, outcome, providerReference, outcome == GatewayOutcome.Failed ? payment?.reason : null);
    }

    public override Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken ct = default)
        => Task.FromResult(new GatewayRefundResult(Success: false, providerReference, "Remboursement Moov non pris en charge pour l'instant."));

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

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.MerchantId}:{_options.ApiKey}"));

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenPath)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await CreateClient().SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(ct)
                ?? throw new InvalidOperationException("Réponse de jeton Moov vide.");

            _cachedToken = token.access_token;
            _tokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(0, token.expires_in - 60));
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    protected override GatewayOutcome MapOutcome(string eventType) => eventType.ToUpperInvariant() switch
    {
        "SUCCESSFUL" or "SUCCESS" or "COMPLETED" => GatewayOutcome.Captured,
        "FAILED" or "REJECTED" or "DECLINED" or "TIMEOUT" or "EXPIRED" or "CANCELLED" => GatewayOutcome.Failed,
        "PENDING" or "ONGOING" or "INITIATED" => GatewayOutcome.Pending,
        _ => GatewayOutcome.Ignored
    };

    protected override string? ExtractReference(JsonElement root)
    {
        foreach (var name in new[] { "transactionId", "reference", "externalReference", "externalId" })
        {
            if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
        }

        return null;
    }

    private static string NormalizeMsisdn(string? msisdn)
        => new string((msisdn ?? string.Empty).Where(char.IsDigit).ToArray());

    private sealed record AccessTokenResponse(string access_token, string token_type, int expires_in);

    private sealed record MoovPaymentResponse(string? transactionId, string? reference, string? status, string? reason);
}
