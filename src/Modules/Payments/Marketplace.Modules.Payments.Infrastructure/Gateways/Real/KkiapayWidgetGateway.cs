using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Real;

/// <summary>
/// KkiaPay, en flux widget.
///
/// KkiaPay NE PUBLIE AUCUNE API REST : ni endpoint de création de transaction, ni
/// page de paiement hébergée, ni interrogation de statut. L'acheteur paie dans un
/// widget JavaScript ouvert par la boutique, et le serveur n'apprend le résultat
/// que par le rappel. Cette passerelle ne fait donc aucun appel réseau : elle
/// prépare les paramètres du widget, puis vérifie et normalise le rappel.
///
/// LA CORRÉLATION PASSE PAR LE CHAMP « data ». L'identifiant de transaction KkiaPay
/// n'existe pas avant le paiement : on ne peut pas le rattacher au paiement à
/// l'initiation. On met donc NOTRE référence dans « data », que KkiaPay rend dans
/// « stateData » — c'est là qu'on la relit.
/// </summary>
public class KkiapayWidgetGateway : IPaymentGateway
{
    private static readonly JsonDocumentOptions JsonOptions = new() { AllowTrailingCommas = true };

    private readonly KkiapayOptions _options;

    public KkiapayWidgetGateway(KkiapayOptions options) => _options = options;

    public string Provider => "Kkiapay";

    /// <summary>Le widget demande lui-même le numéro ; on ne fait que le pré-remplir.</summary>
    public bool RequiresPayerPhone => false;

    /// <summary>
    /// REFUSÉ : aucune page hébergée KkiaPay n'existe vers laquelle rediriger. Lever
    /// est le seul refus que ce port sache exprimer, et un refus bruyant vaut mieux
    /// qu'une redirection vers une URL inventée.
    /// </summary>
    public Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "KkiaPay n'expose pas de page de paiement hébergée : utilisez le flux PaymentIntent, "
            + "qui rend les paramètres du widget à ouvrir côté boutique.");

    /// <summary>
    /// Prépare l'ouverture du widget. Rien n'est envoyé à KkiaPay : les paramètres
    /// partent dans <c>ClientSecret</c> (JSON), que la boutique passe au widget.
    /// </summary>
    public virtual Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken cancellationToken = default)
    {
        var devise = GatewayAmounts.EnsureCurrencyIsSupported(Provider, context.Currency, _options.Currency);
        var montant = GatewayAmounts.ToWholeUnits(Provider, context.Amount, devise);

        // Référence DÉRIVÉE du paiement : unique (un paiement neuf par tentative) et
        // traçable, là où un Guid tiré au hasard ne se relie à rien.
        var reference = $"kkia_{context.PaymentId:N}";

        var parametres = JsonSerializer.Serialize(new
        {
            publicKey = _options.PublicKey,
            amount = montant,
            currency = devise,
            sandbox = _options.Sandbox,
            data = reference,
            callback = string.IsNullOrWhiteSpace(context.ReturnUrl) ? _options.CallbackUrl : context.ReturnUrl,
            phone = context.PayerMsisdn
        });

        return Task.FromResult(new GatewaySession(reference, RedirectUrl: null, parametres));
    }

    public Task<GatewayEvent> ParseWebhookAsync(string rawBody, string? signatureHeader, CancellationToken cancellationToken = default)
    {
        if (!VerifierSecret(signatureHeader))
        {
            return Task.FromResult(new GatewayEvent(Verified: false, GatewayOutcome.Ignored, null, null));
        }

        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody, JsonOptions);
            var racine = document.RootElement;

            var evenement = racine.TryGetProperty("event", out var e) ? e.GetString() ?? string.Empty : string.Empty;
            var resultat = MapOutcome(evenement, racine);

            return Task.FromResult(new GatewayEvent(
                Verified: true,
                resultat,
                ExtractReference(racine),
                FailureReason: resultat == GatewayOutcome.Failed ? RaisonEchec(racine, evenement) : null));
        }
        catch (JsonException)
        {
            return Task.FromResult(new GatewayEvent(Verified: false, GatewayOutcome.Ignored, null, "Payload JSON invalide."));
        }
    }

    /// <summary>
    /// KkiaPay ne documente aucun endpoint d'interrogation : le rappel fait foi. On
    /// renvoie Pending, qui ne change rien à l'état du paiement, plutôt qu'un
    /// « encaissé » inventé.
    ///
    /// CONSÉQUENCE CONNUE : un acheteur qui ferme le widget sans payer ne produit
    /// aucun rappel, le paiement reste Pending, et la réconciliation d'initiation ne
    /// peut pas le débloquer. Fermer ce cas demande une expiration côté domaine,
    /// valable pour tous les prestataires — pas un faux statut ici.
    /// </summary>
    public virtual Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default)
        => Task.FromResult(new GatewayEvent(Verified: true, GatewayOutcome.Pending, providerReference, null));

    /// <summary>
    /// Le remboursement KkiaPay se fait depuis son tableau de bord : aucun endpoint
    /// n'est documenté, et les versements par API sont retirés depuis février 2023.
    /// On échoue explicitement plutôt que de prétendre avoir remboursé.
    /// </summary>
    public virtual Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken cancellationToken = default)
        => Task.FromResult(new GatewayRefundResult(
            Success: false,
            providerReference,
            "Remboursement KkiaPay non automatisable : à effectuer depuis le tableau de bord KkiaPay."));

    /// <summary>
    /// « event » fait foi quand il est présent ; certains rappels ne portent que
    /// « isPaymentSucces » (l'orthographe est celle du prestataire).
    /// </summary>
    private static GatewayOutcome MapOutcome(string evenement, JsonElement racine)
    {
        switch (evenement.Trim().ToLowerInvariant())
        {
            case "transaction.success":
                return GatewayOutcome.Captured;
            case "transaction.failed":
                return GatewayOutcome.Failed;
        }

        if (racine.TryGetProperty("isPaymentSucces", out var reussi)
            && reussi.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return reussi.GetBoolean() ? GatewayOutcome.Captured : GatewayOutcome.Failed;
        }

        // Événement inconnu : acquitté sans rien appliquer au paiement.
        return GatewayOutcome.Ignored;
    }

    /// <summary>
    /// Notre référence revient dans « stateData », qui porte ce qu'on avait mis dans
    /// « data » : une chaîne telle quelle, ou un objet si la boutique y a ajouté ses
    /// propres champs.
    /// </summary>
    private static string? ExtractReference(JsonElement racine)
    {
        if (!racine.TryGetProperty("stateData", out var etat))
        {
            return null;
        }

        if (etat.ValueKind == JsonValueKind.String)
        {
            return etat.GetString();
        }

        if (etat.ValueKind == JsonValueKind.Object)
        {
            foreach (var cle in new[] { "providerReference", "reference" })
            {
                if (etat.TryGetProperty(cle, out var valeur) && valeur.ValueKind == JsonValueKind.String)
                {
                    return valeur.GetString();
                }
            }
        }

        return null;
    }

    private static string RaisonEchec(JsonElement racine, string evenement)
    {
        var message = racine.TryGetProperty("failureMessage", out var m) ? m.GetString() : null;
        var code = racine.TryGetProperty("failureCode", out var c) ? c.GetString() : null;

        if (!string.IsNullOrWhiteSpace(message))
        {
            return string.IsNullOrWhiteSpace(code) ? message! : $"{message} ({code})";
        }

        return string.IsNullOrWhiteSpace(code) ? evenement : code!;
    }

    /// <summary>
    /// KkiaPay ne SIGNE pas le corps : il renvoie le secret du tableau de bord dans
    /// l'en-tête x-kkiapay-secret. On compare donc en temps constant, sans HMAC.
    /// Secret absent : rejeté, sauf en Development
    /// (<see cref="GatewayWebhook.AllowUnsignedWhenSecretMissing"/>).
    ///
    /// LIMITE DU PROCÉDÉ : le corps n'étant pas signé, un rappel capturé puis rejoué
    /// avec le bon en-tête reste acceptable. L'application du résultat est idempotente,
    /// ce qui borne les dégâts à un rejeu sans effet.
    /// </summary>
    private bool VerifierSecret(string? entete)
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret))
        {
            return GatewayWebhook.AllowUnsignedWhenSecretMissing;
        }

        if (string.IsNullOrWhiteSpace(entete))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(entete.Trim()), Encoding.UTF8.GetBytes(_options.WebhookSecret));
    }
}
