using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Real;

/// <summary>
/// Adaptateur MTN Mobile Money RÉEL (Collection API). Flux : on obtient un jeton
/// OAuth (Basic ApiUser:ApiKey), puis on lance un RequestToPay identifié par un
/// X-Reference-Id (la référence de corrélation), et on lit le statut par GET.
/// Le jeton est mis en cache jusqu'à sa quasi-expiration.
///
/// Branche tes identifiants sandbox dans « Payments:MtnMomo » : dès qu'ils sont
/// renseignés, l'installer remplace le stub par cet adaptateur.
/// </summary>
public sealed class MtnMomoHttpGateway : HttpPaymentGatewayBase
{
    private readonly MtnMomoOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTime _tokenExpiresAtUtc = DateTime.MinValue;

    public MtnMomoHttpGateway(IHttpClientFactory httpClientFactory, MtnMomoOptions options)
        : base(httpClientFactory) => _options = options;

    public const string ClientName = "mtn-momo";

    public override string Provider => "MtnMomo";
    public override bool RequiresPayerPhone => true;

    protected override string HttpClientName => ClientName;
    protected override string WebhookSecret => _options.WebhookSecret;
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
        // Le contrôle est fait AVANT le jeton : refuser ne doit pas coûter un
        // aller-retour OAuth, et surtout pas laisser croire qu'on a commencé quelque
        // chose côté prestataire.
        var currency = EnsureCurrencyIsSupported(Provider, context.Currency, _options.Currency);

        var token = await GetAccessTokenAsync(ct);
        var referenceId = Guid.NewGuid().ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "collection/v1_0/requesttopay")
        {
            Content = JsonContent.Create(new
            {
                // « amount » est une CHAÎNE chez MoMo : on la formate à la précision de la
                // devise (« 5000 » en XOF, « 50.00 » en EUR) au lieu de tronquer à l'entier.
                amount = FormatAmountForCurrency(context.Amount, currency),
                currency,
                externalId = context.OrderId.ToString(),
                payer = new
                {
                    partyIdType = "MSISDN",
                    partyId = NormalizeMsisdn(context.PayerMsisdn)
                },
                payerMessage = "Paiement marketplace",
                payeeNote = $"Commande {context.OrderId}"
            })
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Reference-Id", referenceId);
        request.Headers.Add("X-Target-Environment", _options.TargetEnvironment);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.SubscriptionKey);
        if (!string.IsNullOrWhiteSpace(_options.CallbackUrl))
        {
            request.Headers.Add("X-Callback-Url", _options.CallbackUrl);
        }

        var response = await CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        // RequestToPay renvoie 202 sans corps : la référence est notre X-Reference-Id.
        return new GatewaySession(referenceId, RedirectUrl: null, ClientSecret: null);
    }

    public override async Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"collection/v1_0/requesttopay/{providerReference}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Target-Environment", _options.TargetEnvironment);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.SubscriptionKey);

        var response = await CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<RequestToPayStatus>(ct);
        var outcome = MapOutcome(status?.status ?? string.Empty);
        return new GatewayEvent(Verified: true, outcome, providerReference, outcome == GatewayOutcome.Failed ? status?.reason : null);
    }

    public override Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken ct = default)
        // Le remboursement MoMo passe par le produit Disbursement (API distincte),
        // non couvert ici. À implémenter quand le payout vendeur sera branché.
        => Task.FromResult(new GatewayRefundResult(Success: false, providerReference, "Remboursement MoMo non pris en charge (API Disbursement requise)."));

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

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiUser}:{_options.ApiKey}"));

            using var request = new HttpRequestMessage(HttpMethod.Post, "collection/token/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Headers.Add("Ocp-Apim-Subscription-Key", _options.SubscriptionKey);

            var response = await CreateClient().SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(ct)
                ?? throw new InvalidOperationException("Réponse de jeton MoMo vide.");

            _cachedToken = token.access_token;
            // Marge de 60 s pour ne pas utiliser un jeton expiré en bordure.
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
        "FAILED" or "REJECTED" or "TIMEOUT" or "EXPIRED" or "CANCELLED" => GatewayOutcome.Failed,
        "PENDING" or "ONGOING" => GatewayOutcome.Pending,
        _ => GatewayOutcome.Ignored
    };

    protected override string? ExtractReference(JsonElement root)
    {
        if (root.TryGetProperty("referenceId", out var refId))
        {
            return refId.GetString();
        }

        return root.TryGetProperty("externalId", out var extId) ? extId.GetString() : null;
    }

    private static string NormalizeMsisdn(string? msisdn)
        => new string((msisdn ?? string.Empty).Where(char.IsDigit).ToArray());

    private sealed record AccessTokenResponse(string access_token, string token_type, int expires_in);

    private sealed record RequestToPayStatus(string? status, string? reason);
}
