using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Real;

/// <summary>
/// Base des adaptateurs PSP qui font de vrais appels réseau. Contrairement à
/// <see cref="SimulatedPaymentGateway"/> (qui ne touche pas le réseau), cette base
/// résout un <see cref="HttpClient"/> nommé via <see cref="IHttpClientFactory"/>
/// (durée de vie des handlers gérée par le framework — pas de socket exhaustion)
/// et mutualise le parsing/vérification des webhooks. Chaque PSP implémente ses
/// appels HTTP (token, charge, statut, remboursement).
/// </summary>
public abstract class HttpPaymentGatewayBase : IPaymentGateway
{
    private readonly IHttpClientFactory _httpClientFactory;

    protected HttpPaymentGatewayBase(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public abstract string Provider { get; }

    public virtual bool RequiresPayerPhone => false;

    /// <summary>Nom du client HTTP enregistré (AddHttpClient) pour ce PSP.</summary>
    protected abstract string HttpClientName { get; }

    /// <summary>Secret de signature des webhooks ; vide = sandbox permissif.</summary>
    protected abstract string WebhookSecret { get; }

    /// <summary>
    /// Adresse de base de l'API du prestataire.
    ///
    /// ELLE VIENT DES OPTIONS, PLUS DE L'ENREGISTREMENT. Avant, `AddHttpClient`
    /// posait la BaseAddress au démarrage, à partir d'une configuration figée. Les
    /// réglages se modifiant depuis la console d'administration, l'adresse est
    /// désormais lue sur l'instance d'options que le résolveur vient de construire.
    /// </summary>
    protected abstract string BaseUrl { get; }

    /// <summary>Champ du payload portant le type/statut d'événement.</summary>
    protected abstract string EventTypeField { get; }

    protected HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        // La barre oblique finale n'est pas cosmétique : sans elle, `new Uri(base,
        // "v1/charges")` REMPLACE le dernier segment du chemin de base au lieu de s'y
        // ajouter. « …/v1 » + « transactions » donnerait « …/transactions ».
        if (client.BaseAddress is null && !string.IsNullOrWhiteSpace(BaseUrl))
        {
            client.BaseAddress = new Uri(BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/");
        }

        return client;
    }

    public abstract Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken cancellationToken = default);

    public abstract Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken cancellationToken = default);

    public abstract Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default);

    public abstract Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken cancellationToken = default);

    public virtual Task<GatewayEvent> ParseWebhookAsync(string rawBody, string? signatureHeader, CancellationToken cancellationToken = default)
        => Task.FromResult(GatewayWebhook.Parse(rawBody, signatureHeader, WebhookSecret, EventTypeField, MapOutcome, ExtractReference));

    /// <summary>Mappe un type/statut d'événement PSP vers un résultat normalisé.</summary>
    protected abstract GatewayOutcome MapOutcome(string eventType);

    /// <summary>Extrait l'identifiant de corrélation du payload de webhook.</summary>
    protected abstract string? ExtractReference(JsonElement root);

    /// <summary>Voir <see cref="GatewayAmounts.EnsureCurrencyIsSupported"/>.</summary>
    protected static string EnsureCurrencyIsSupported(string provider, string? requestedCurrency, string? configuredCurrency)
        => GatewayAmounts.EnsureCurrencyIsSupported(provider, requestedCurrency, configuredCurrency);

    /// <summary>Voir <see cref="GatewayAmounts.ToWholeUnits"/>.</summary>
    protected static long ToWholeUnits(string provider, decimal amount, string currency)
        => GatewayAmounts.ToWholeUnits(provider, amount, currency);

    /// <summary>Voir <see cref="GatewayAmounts.FormatAmountForCurrency"/>.</summary>
    protected static string FormatAmountForCurrency(decimal amount, string currency)
        => GatewayAmounts.FormatAmountForCurrency(amount, currency);
}
