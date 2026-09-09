using System.Globalization;
using System.Text.Json;
using Marketplace.BuildingBlocks.Domain.Primitives;
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

    /// <summary>Champ du payload portant le type/statut d'événement.</summary>
    protected abstract string EventTypeField { get; }

    protected HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);

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

    /// <summary>
    /// ══════════════════════════════════════════════════════════════════════════════
    /// ON N'ENCAISSE PAS DANS UNE DEVISE QU'ON NE SAIT PAS TRAITER.
    ///
    /// FedaPay, MTN MoMo et Moov envoyaient au prestataire la devise lue dans la
    /// CONFIGURATION (« Payments:*:Currency ») avec le montant lu dans la COMMANDE.
    /// Les deux ne se rencontraient nulle part. Une commande de 50,00 EUR partait donc
    /// comme une charge de 50 XOF : la valeur numérique de la commande, l'étiquette de
    /// la configuration. L'acheteur payait environ un trente-troisième du dû, et le
    /// paiement était ensuite marqué ENCAISSÉ pour le montant complet — c'est-à-dire
    /// une commande livrée contre presque rien, sans qu'aucun écran ne le signale.
    ///
    /// POURQUOI REFUSER PLUTÔT QUE CONVERTIR. Convertir supposerait un taux de change :
    /// lequel, à quelle date, avec quelle marge, et opposable à qui ? Aucune de ces
    /// questions n'a de réponse dans ce module. Une conversion inventée est une erreur
    /// silencieuse de plus, du même genre que celle qu'on corrige.
    ///
    /// POURQUOI REFUSER PLUTÔT QU'ACCEPTER TEL QUEL. Accepter en silence EST le défaut.
    /// Ces trois passerelles sont mono-devise par contrat (compte marchand ouvert dans
    /// une devise) : leur envoyer autre chose produit soit un rejet du prestataire,
    /// soit — et c'est le cas constaté — un encaissement faux.
    ///
    /// POURQUOI UNE EXCEPTION ET PAS UN Result. Le port <c>IPaymentGateway</c> renvoie
    /// une <c>GatewaySession</c>, pas un <c>Result</c> : l'exception est le seul refus
    /// que ce contrat sait exprimer, et c'est déjà celui qu'utilisent ces adaptateurs
    /// pour toute réponse inexploitable du prestataire. L'appelant n'en attrape aucune,
    /// donc l'initiation échoue en erreur serveur — bruyante, tracée, et surtout SANS
    /// paiement créé côté prestataire. C'est le comportement voulu : mieux vaut un
    /// refus visible qu'un encaissement faux réputé réussi.
    /// ══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    protected static string EnsureCurrencyIsSupported(string provider, string? requestedCurrency, string? configuredCurrency)
    {
        var requested = (requestedCurrency ?? string.Empty).Trim().ToUpperInvariant();
        var configured = (configuredCurrency ?? string.Empty).Trim().ToUpperInvariant();

        if (requested.Length == 0)
        {
            throw new InvalidOperationException(
                $"{provider} : la commande ne porte aucune devise. Aucun encaissement n'est tenté.");
        }

        // Configuration vide : on ne DEVINE pas que le compte marchand est en XOF. Une
        // devise implicite est exactement ce qui a produit le défaut d'origine.
        if (configured.Length == 0)
        {
            throw new InvalidOperationException(
                $"{provider} : aucune devise configurée pour cette passerelle. " +
                $"Renseigne « Payments:{provider}:Currency » avant d'encaisser du {requested}.");
        }

        if (!string.Equals(requested, configured, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{provider} : la commande est libellée en {requested}, cette passerelle est configurée en {configured}. " +
                "L'encaissement est REFUSÉ — envoyer le montant de la commande sous l'étiquette de la devise " +
                "configurée ferait payer un montant sans rapport avec ce qui est dû.");
        }

        return requested;
    }

    /// <summary>
    /// Montant en unités ENTIÈRES, pour les prestataires dont le champ « amount » est un
    /// entier (FedaPay). Refuse une devise à décimales : la tronquer silencieusement est
    /// la seconde moitié du défaut — 8 999,10 envoyés comme 8 999, la commande restant
    /// enregistrée à 8 999,10 et marquée payée.
    ///
    /// Depuis que <see cref="Money"/> arrondit selon la devise, un montant issu du
    /// domaine tombe déjà sur la grille : ce garde-fou n'a plus rien à tronquer, il
    /// vérifie seulement que personne ne l'a contourné.
    /// </summary>
    protected static long ToWholeUnits(string provider, decimal amount, string currency)
    {
        if (!Money.IsZeroDecimal(currency))
        {
            throw new InvalidOperationException(
                $"{provider} : la devise {currency} admet des décimales, or le prestataire n'accepte qu'un montant entier. " +
                "L'encaissement est REFUSÉ plutôt que tronqué.");
        }

        return (long)Money.Round(amount, currency);
    }

    /// <summary>
    /// Montant formaté avec le nombre de décimales de SA devise, pour les prestataires
    /// dont le champ « amount » est une chaîne (MTN MoMo, Moov) : « 5000 » en XOF,
    /// « 50.00 » en EUR. C'était <c>(long)Math.Round(amount)</c>, donc une troncature à
    /// l'entier appliquée à toute devise, centimes compris.
    /// </summary>
    protected static string FormatAmountForCurrency(decimal amount, string currency)
        => Money.Round(amount, currency)
            .ToString("F" + Money.DecimalsFor(currency), CultureInfo.InvariantCulture);
}
