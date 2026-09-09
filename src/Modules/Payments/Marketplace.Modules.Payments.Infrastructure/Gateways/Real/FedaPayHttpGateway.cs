using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
        // LA DEVISE VIENT DE LA COMMANDE, ET ELLE DOIT ÊTRE CELLE DU COMPTE MARCHAND.
        // Le payload envoyait `_options.Currency` — l'étiquette de la configuration —
        // avec `context.Amount`, le montant de la commande. Voir l'encadré de
        // `EnsureCurrencyIsSupported` : c'est ce couple dépareillé qui faisait payer
        // 50 XOF une commande de 50,00 EUR.
        var currency = EnsureCurrencyIsSupported(Provider, context.Currency, _options.Currency);

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
                // Le champ « amount » de FedaPay est un ENTIER : la garde refuse une devise
                // à décimales au lieu de la tronquer.
                amount = ToWholeUnits(Provider, context.Amount, currency),
                currency = new { iso = currency },
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
        if (!VerifyFedaPaySignature(rawBody, signatureHeader, WebhookSecret))
        {
            return Task.FromResult(new GatewayEvent(Verified: false, GatewayOutcome.Ignored, null, null));
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
            var root = doc.RootElement;

            // GARDE : les événements de DÉPÔT (payout.*) arrivent sur la même URL et
            // portent un id d'entité distinct — mais numériquement collisionnable avec
            // celui d'une transaction. Sans ce filtre, un « payout.canceled » ferait
            // échouer le PAIEMENT qui porte le même numéro. Ils sont traités ailleurs.
            var eventName = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
            if (eventName.StartsWith("payout", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new GatewayEvent(Verified: true, GatewayOutcome.Ignored, null, null));
            }

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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// VÉRIFICATION DE LA SIGNATURE DU WEBHOOK FEDAPAY.
    ///
    /// Le header « x-fedapay-signature » est au format « t=&lt;horodatage&gt;,s=&lt;hmac_hex&gt; »
    /// (comme Stripe) : la signature est le HMAC-SHA256 hex de « &lt;t&gt;.&lt;corps_brut&gt; ».
    /// Repli : un header réduit à une signature hex brute retombe sur la vérification
    /// générique HMAC(corps).
    ///
    /// SECRET MANQUANT : ON REJETTE. Cette méthode retournait `true` — un
    /// fail-open. Comme cette passerelle SURCHARGE `ParseWebhookAsync`, elle ne
    /// passait jamais par `GatewayWebhook.Parse`, seul endroit qui consultait le
    /// garde-fou : celui-ci était donc écrit, commenté, fermé en production… et
    /// contourné par les deux seules passerelles réellement déployées.
    ///
    /// Ce que cela permettait : un POST anonyme sur une route sans limitation de
    /// débit, portant `{"name":"transaction.approved", ...}`, déclenchait la chaîne
    /// complète — stock décrémenté, expéditions créées, gains vendeur crédités.
    /// Une commande entièrement payée sans qu'un franc ait bougé. Et le même défaut
    /// atteignait les VERSEMENTS, `FedaPayPayoutGateway` réutilisant cette méthode :
    /// un faux « payout.sent » soldait un retrait jamais versé.
    ///
    /// La seule protection était un `${VAR:?}` dans le compose de production. Un
    /// fichier de déploiement n'est pas un garde-fou de sécurité.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    internal static bool VerifyFedaPaySignature(string rawBody, string? signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return GatewayWebhook.AllowUnsignedWhenSecretMissing;
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        string? t = null, sig = null;
        foreach (var part in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            var key = kv[0].Trim();
            var val = kv[1].Trim();
            if (key == "t") t = val;
            else if (key is "s" or "s1" or "v1") sig = val;
        }

        // Header sans « t= » : signature hex brute → vérification générique.
        if (sig is null)
        {
            return GatewayWebhook.VerifySignature(rawBody, signatureHeader, secret);
        }

        if (t is null)
        {
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // L'HORODATAGE EST SIGNÉ, ENCORE FAUT-IL LE REGARDER.
        //
        // `t` entrait dans le HMAC mais n'était jamais confronté à l'heure courante :
        // un corps signé capturé restait rejouable indéfiniment. La fenêtre est
        // vérifiée sur la valeur PARSÉE, la signature sur la chaîne REÇUE — c'est ce
        // qui évite le piège « 007 vaut 7 » : un attaquant qui reformate l'horodatage
        // casse la signature avant d'atteindre ce contrôle.
        // ─────────────────────────────────────────────────────────────────────────
        var expected = GatewayWebhook.ComputeSignature($"{t}.{rawBody}", secret);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(sig.ToLowerInvariant())))
        {
            return false;
        }

        return GatewayWebhook.IsWithinClockSkew(t);
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
