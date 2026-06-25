using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;

/// <summary>
/// Base des adaptateurs Mobile Money (MTN MoMo, Moov) en mode stub sandbox. Le
/// flux n'est ni une redirection ni un client secret : c'est un RequestToPay —
/// on transmet le montant et le numéro du payeur au PSP, l'acheteur approuve sur
/// son téléphone, puis le PSP notifie par callback (webhook) ou on interroge le
/// statut. Les deux flux logiques (checkout / intent) y mènent au même RequestToPay.
///
/// Pour passer en réel : remplacer RequestToPay par l'appel HTTP Collection MoMo
/// (POST /collection/v1_0/requesttopay, X-Reference-Id) ou l'API Moov, et la
/// vérification de signature par le schéma du PSP.
/// </summary>
public abstract class MobileMoneyPaymentGateway : SimulatedPaymentGateway
{
    /// <summary>Le Mobile Money exige le numéro du payeur (MSISDN).</summary>
    public override bool RequiresPayerPhone => true;

    /// <summary>Préfixe de la référence RequestToPay.</summary>
    protected abstract string ReferencePrefix { get; }

    // Les notions « checkout hébergé » / base d'URL ne s'appliquent pas au Mobile Money.
    protected override string CheckoutPrefix => ReferencePrefix;
    protected override string IntentPrefix => ReferencePrefix;
    protected override string CheckoutBaseUrl => string.Empty;

    // Le payload de callback Mobile Money porte le statut dans « status ».
    protected override string EventTypeField => "status";

    public override Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken cancellationToken = default)
        => RequestToPayAsync(context);

    public override Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken cancellationToken = default)
        => RequestToPayAsync(context);

    private Task<GatewaySession> RequestToPayAsync(GatewayChargeContext context)
    {
        // Token

        // TODO réel : POST RequestToPay (montant, devise, MSISDN) ; la référence
        // est l'X-Reference-Id (MoMo) / la transaction id (Moov). Ici on simule.
        var reference = $"{ReferencePrefix}-{Guid.NewGuid():N}";
        // Ni redirection ni client secret : l'acheteur approuve sur son téléphone,
        // le client interroge ensuite /return/{id} ou attend le webhook.
        return Task.FromResult(new GatewaySession(reference, RedirectUrl: null, ClientSecret: null));
    }

    async Task<string> GetAccessTokenAsync(bool isLive,CancellationToken ct)
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                $"{"apiuser"}:{"GetApiKey"}"));

        using var request = new HttpRequestMessage(HttpMethod.Post, "collection/token/");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Add("Ocp-Apim-Subscription-Key", "GetSubscriptionKey");

        var http = new HttpClient();
        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var token = await response.Content
                        .ReadFromJsonAsync<MtnAccessTokenResponse>(ct)
                    ?? throw new InvalidOperationException("MTN token response was null.");

        return token.access_token;
    }

    public override Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default)
        // TODO réel : GET .../requesttopay/{referenceId} et lire « status ».
        // En sandbox, on considère la demande approuvée par le payeur.
        => Task.FromResult(new GatewayEvent(Verified: true, GatewayOutcome.Captured, providerReference, null));

    protected override GatewayOutcome MapOutcome(string eventType) => eventType.ToUpperInvariant() switch
    {
        "SUCCESSFUL" or "SUCCESS" or "COMPLETED" => GatewayOutcome.Captured,
        "FAILED" or "REJECTED" or "TIMEOUT" or "EXPIRED" or "CANCELLED" => GatewayOutcome.Failed,
        "REFUNDED" => GatewayOutcome.Refunded,
        "PENDING" or "ONGOING" => GatewayOutcome.Pending,
        _ => GatewayOutcome.Ignored
    };

    protected override string? ExtractReference(JsonElement root)
    {
        // Payload de test / natif : referenceId (MoMo) ou externalId, sinon le champ générique.
        if (root.TryGetProperty("referenceId", out var refId))
        {
            return refId.GetString();
        }

        if (root.TryGetProperty("externalId", out var extId))
        {
            return extId.GetString();
        }

        return root.TryGetProperty("providerReference", out var direct) ? direct.GetString() : null;
    }
}

public sealed record MtnAccessTokenResponse(string access_token, string token_type, int    expires_in);

