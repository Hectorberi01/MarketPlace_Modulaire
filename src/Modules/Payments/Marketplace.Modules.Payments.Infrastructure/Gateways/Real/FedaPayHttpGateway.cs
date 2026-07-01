using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Real;

/// <summary>
/// Adaptateur FedaPay RÉEL (page de paiement hébergée). Flux :
///  1. POST /transactions → crée la transaction (montant, devise, callback).
///  2. POST /transactions/{id}/token → renvoie l'URL de paiement (Mobile Money + carte).
///  3. L'acheteur paie sur la page FedaPay ; on confirme par webhook signé
///     (x-fedapay-signature) ou en interrogeant GET /transactions/{id}.
///
/// Branche ta clé secrète dans « Payments:FedaPay » : dès qu'elle est renseignée,
/// l'installer remplace le stub par cet adaptateur.
/// </summary>
public sealed class FedaPayHttpGateway : HttpPaymentGatewayBase
{
    private readonly FedaPayOptions _options;

    public FedaPayHttpGateway(IHttpClientFactory httpClientFactory, FedaPayOptions options)
        : base(httpClientFactory) => _options = options;

    public const string ClientName = "fedapay";

    public override string Provider => "FedaPay";

    // La page hébergée collecte elle-même le numéro / la carte : pas besoin du MSISDN en amont.
    public override bool RequiresPayerPhone => false;

    protected override string HttpClientName => ClientName;
    protected override string WebhookSecret => _options.WebhookSecret;
    protected override string EventTypeField => "status";

    public override Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken ct = default)
        => CreateHostedAsync(context, ct);

    public override Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken ct = default)
        => CreateHostedAsync(context, ct);

    private async Task<GatewaySession> CreateHostedAsync(GatewayChargeContext context, CancellationToken ct)
    {
        // FedaPay valide callback_url comme une URL http(s) : on ne lui transmet
        // jamais un schéma applicatif (marketplace://…). On préfère l'URL configurée
        // (https) si présente, sinon on omet le champ (le retour est géré par la
        // WebView + l'interrogation du statut).
        var callbackUrl = HttpUrlOrNull(_options.CallbackUrl) ?? HttpUrlOrNull(context.ReturnUrl);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "transactions")
        {
            Content = JsonContent.Create(new
            {
                description = $"Commande {context.OrderId}",
                amount = (long)Math.Round(context.Amount),
                currency = new { iso = _options.Currency },
                callback_url = callbackUrl,
                custom_metadata = new
                {
                    payment_id = context.PaymentId.ToString(),
                    order_id = context.OrderId.ToString()
                }
            })
        };
        Authorize(createRequest);

        var createResponse = await CreateClient().SendAsync(createRequest, ct);
        await EnsureFedaPaySuccessAsync(createResponse, "création de la transaction", ct);

        var transactionId = ExtractTransactionId(await createResponse.Content.ReadAsStringAsync(ct))
            ?? throw new InvalidOperationException("FedaPay : identifiant de transaction introuvable dans la réponse.");

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"transactions/{transactionId}/token");
        Authorize(tokenRequest);

        var tokenResponse = await CreateClient().SendAsync(tokenRequest, ct);
        await EnsureFedaPaySuccessAsync(tokenResponse, "génération du lien de paiement", ct);

        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        if (token is null || string.IsNullOrWhiteSpace(token.url))
        {
            throw new InvalidOperationException("FedaPay : URL de paiement absente de la réponse token.");
        }

        return new GatewaySession(transactionId, RedirectUrl: token.url, ClientSecret: null);
    }

    public override async Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"transactions/{providerReference}");
        Authorize(request);

        var response = await CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var status = ExtractStatus(await response.Content.ReadAsStringAsync(ct)) ?? string.Empty;
        var outcome = MapOutcome(status);
        return new GatewayEvent(Verified: true, outcome, providerReference, outcome == GatewayOutcome.Failed ? status : null);
    }

    public override Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken ct = default)
        // Le remboursement FedaPay se pilote depuis le tableau de bord (pas d'endpoint
        // public dans l'API REST documentée) : non couvert ici.
        => Task.FromResult(new GatewayRefundResult(Success: false, providerReference, "Remboursement FedaPay non pris en charge via l'API (tableau de bord requis)."));

    /// <summary>
    /// Webhook FedaPay : payload de forme { name, entity:{ id, status } }. On lit le
    /// statut dans l'entité (et non au premier niveau), d'où l'override.
    /// </summary>
    public override Task<GatewayEvent> ParseWebhookAsync(string rawBody, string? signatureHeader, CancellationToken ct = default)
    {
        if (!GatewayWebhook.VerifySignature(rawBody, signatureHeader, WebhookSecret))
        {
            return Task.FromResult(new GatewayEvent(Verified: false, GatewayOutcome.Ignored, null, null));
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
            var root = doc.RootElement;
            var entity = root.TryGetProperty("entity", out var e) ? e : root;

            var status = entity.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty;
            var reference = ExtractReference(entity);
            var outcome = MapOutcome(status);

            return Task.FromResult(new GatewayEvent(Verified: true, outcome, reference, outcome == GatewayOutcome.Failed ? status : null));
        }
        catch (JsonException)
        {
            return Task.FromResult(new GatewayEvent(Verified: false, GatewayOutcome.Ignored, null, "Payload JSON invalide."));
        }
    }

    protected override GatewayOutcome MapOutcome(string eventType) => eventType.ToLowerInvariant() switch
    {
        "approved" or "transferred" => GatewayOutcome.Captured,
        "declined" or "canceled" or "cancelled" => GatewayOutcome.Failed,
        "pending" => GatewayOutcome.Pending,
        "refunded" => GatewayOutcome.Refunded,
        _ => GatewayOutcome.Ignored
    };

    protected override string? ExtractReference(JsonElement root)
    {
        if (root.TryGetProperty("id", out var id))
        {
            return id.ValueKind == JsonValueKind.Number ? id.GetInt64().ToString() : id.GetString();
        }

        return null;
    }

    private void Authorize(HttpRequestMessage request)
        => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

    /// <summary>Ne retient une URL que si elle est en http(s) (FedaPay refuse les schémas applicatifs).</summary>
    private static string? HttpUrlOrNull(string? url)
        => !string.IsNullOrWhiteSpace(url)
           && Uri.TryCreate(url, UriKind.Absolute, out var u)
           && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            ? url
            : null;

    /// <summary>
    /// Vérifie la réponse FedaPay et, en cas d'échec, lève une erreur explicite
    /// incluant le corps renvoyé (utile pour diagnostiquer 422 / validation).
    /// </summary>
    private static async Task EnsureFedaPaySuccessAsync(HttpResponseMessage response, string step, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException($"FedaPay — échec {step} ({(int)response.StatusCode}) : {body}");
    }

    /// <summary>
    /// FedaPay enveloppe parfois ses réponses sous « v1/transaction ». On lit l'id
    /// que la réponse soit enveloppée ou plate.
    /// </summary>
    private static string? ExtractTransactionId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var tx = Unwrap(doc.RootElement);
        if (tx.TryGetProperty("id", out var id))
        {
            return id.ValueKind == JsonValueKind.Number ? id.GetInt64().ToString() : id.GetString();
        }

        return null;
    }

    private static string? ExtractStatus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var tx = Unwrap(doc.RootElement);
        return tx.TryGetProperty("status", out var s) ? s.GetString() : null;
    }

    private static JsonElement Unwrap(JsonElement root)
        => root.TryGetProperty("v1/transaction", out var wrapped) ? wrapped : root;

    private sealed record TokenResponse(string? token, string? url);
}
