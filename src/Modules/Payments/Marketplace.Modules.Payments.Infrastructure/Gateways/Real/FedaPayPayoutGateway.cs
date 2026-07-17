using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways;
using Microsoft.Extensions.Logging;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Real;

/// <summary>
/// Reversement (dépôt) RÉEL via FedaPay. Flux en deux temps :
///  1. POST /payouts       → crée le dépôt (statut « pending »).
///  2. PUT  /payouts/start → déclenche l'envoi (statut « started »).
///
/// ⚠️ Un « start » accepté ne prouve PAS que l'argent est arrivé. Le cycle de vie
/// FedaPay est pending → started → processing → sent | failed. Seul « sent » est une
/// preuve de versement. D'où <see cref="GetStatusAsync"/>, utilisé par la
/// réconciliation pour clore (ou rembourser) le retrait.
/// </summary>
public sealed class FedaPayPayoutGateway : IPayoutGateway
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FedaPayOptions _options;
    private readonly ILogger<FedaPayPayoutGateway> _logger;

    public FedaPayPayoutGateway(
        IHttpClientFactory httpClientFactory,
        FedaPayOptions options,
        ILogger<FedaPayPayoutGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Traduit un refus du PSP en message destiné au VENDEUR, et journalise le détail
    /// technique.
    ///
    /// Le motif d'échec d'un retrait est stocké dans <c>Withdrawal.FailureReason</c> et
    /// affiché tel quel dans l'app vendeur. Y recopier le corps HTTP de FedaPay revenait
    /// à montrer au vendeur « {"message":"Opération non autorisée","errors":{}} » : il n'y
    /// comprend rien, ne peut rien y faire, et cela expose les entrailles de notre
    /// intégration PSP. Le détail va donc dans les logs (pour nous), le sens va au
    /// vendeur (pour lui).
    /// </summary>
    private string Explain(HttpStatusCode status, string body, string step)
    {
        _logger.LogError(
            "FedaPay payout — {Step} refusé ({StatusCode}). Réponse : {Body}",
            step, (int)status, body);

        return status switch
        {
            // 401/403 ne parlent JAMAIS du vendeur : c'est NOTRE compte marchand qui n'est
            // pas habilité aux dépôts (fonctionnalité à activer chez FedaPay, ou clé sans
            // la portée « payouts »). Le vendeur n'a rien à corriger — inutile de le culpabiliser.
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "Versement impossible pour le moment : notre compte de reversement n'est pas autorisé "
                + "par l'opérateur. Vos fonds ont été recrédités et nos équipes sont alertées.",

            // 422 / 400 : le plus souvent le numéro Mobile Money du vendeur — là, il PEUT agir.
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                "Versement refusé par l'opérateur. Vérifiez le numéro Mobile Money de votre compte "
                + "de reversement (opérateur et indicatif pays), puis réessayez.",

            _ => "Versement refusé par l'opérateur. Vos fonds ont été recrédités sur votre solde.",
        };
    }

    public async Task<PayoutResult> SendAsync(PayoutInstruction instruction, CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendInternalAsync(instruction, cancellationToken);
        }
        catch (Exception ex)
        {
            // Exception réseau / timeout / parsing : issue INDÉTERMINÉE. Le dépôt est
            // peut-être parti chez FedaPay. On ne renvoie donc PAS « Failed » (qui
            // déclencherait un remboursement, puis potentiellement un second versement) :
            // la réconciliation tranchera.
            _logger.LogError(ex, "FedaPay payout — issue indéterminée (réf. {Reference}).", instruction.Reference);

            return PayoutResult.Unknown(
                "Versement en cours de vérification auprès de l'opérateur. "
                + "Le statut sera mis à jour automatiquement.");
        }
    }

    private async Task<PayoutResult> SendInternalAsync(PayoutInstruction instruction, CancellationToken cancellationToken)
    {
        // Routage : l'opérateur du vendeur détermine le « mode » FedaPay ET le pays.
        // Un opérateur non routable est un échec DÉFINITIF (rien n'est parti) : on
        // préfère refuser que d'expédier un numéro sénégalais vers MTN Bénin.
        var route = ResolveRoute(instruction.Beneficiary.Provider);
        if (route is null)
        {
            return PayoutResult.Failed(
                $"Opérateur « {instruction.Beneficiary.Provider} » non pris en charge pour les versements. " +
                "Le vendeur doit configurer un opérateur supporté (MTN Bénin, Moov Bénin, Celtis).");
        }

        var (mode, country) = route.Value;
        var client = _httpClientFactory.CreateClient(FedaPayHttpGateway.ClientName);
        var (firstName, lastName) = SplitName(instruction.Beneficiary.Name);

        // FedaPay exige le numéro en E.164 avec « + » (ex. « +22997808080 »). Sans le
        // « + », la détection d'opérateur échoue et la création part en erreur.
        var number = ToE164(instruction.Beneficiary.Msisdn, country);
        if (string.IsNullOrEmpty(number))
        {
            return PayoutResult.Failed("Numéro Mobile Money du bénéficiaire absent ou invalide.");
        }

        // 1. Création du dépôt (statut « pending »).
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "payouts")
        {
            Content = JsonContent.Create(new
            {
                // Le montant DOIT être un entier (XOF n'a pas de décimales).
                amount = (long)Math.Round(instruction.Amount),
                currency = new { iso = instruction.Currency },
                mode,
                description = "Reversement marchand HbaExpress",
                customer = new
                {
                    firstname = firstName,
                    lastname = lastName,
                    // Email stable par bénéficiaire : FedaPay identifie le client par
                    // email ; un email dérivé du numéro évite de recréer un client à
                    // chaque versement. Domaine que nous contrôlons (jamais un tiers).
                    email = BuildEmail(number),
                    phone_number = new { number, country }
                },
                merchant_reference = instruction.Reference
            })
        };
        Authorize(createRequest);

        var createResponse = await client.SendAsync(createRequest, cancellationToken);
        var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!createResponse.IsSuccessStatusCode)
        {
            // 4xx = rejet argumenté par FedaPay → rien n'est parti → échec définitif.
            // 5xx = panne côté PSP → on ne peut RIEN affirmer → indéterminé.
            var reason = Explain(createResponse.StatusCode, createBody, "création");

            return IsDefinitiveRejection(createResponse.StatusCode)
                ? PayoutResult.Failed(reason)
                : PayoutResult.Unknown(
                    "Versement en cours de vérification auprès de l'opérateur. "
                    + "Le statut sera mis à jour automatiquement.");
        }

        var payoutId = ExtractPayoutId(createBody);
        if (payoutId is null)
        {
            // Le dépôt EXISTE peut-être (la création a réussi) mais on n'a pas son id :
            // impossible de le démarrer ni de le réconcilier. Indéterminé, jamais un échec.
            return PayoutResult.Unknown("FedaPay : identifiant de dépôt absent de la réponse de création.");
        }

        // 2. Démarrage du dépôt : corps = { "payouts": [ { id } ] }.
        using var startRequest = new HttpRequestMessage(HttpMethod.Put, "payouts/start")
        {
            Content = JsonContent.Create(new
            {
                payouts = new object[] { new { id = payoutId } }
            })
        };
        Authorize(startRequest);

        var startResponse = await client.SendAsync(startRequest, cancellationToken);
        if (!startResponse.IsSuccessStatusCode)
        {
            var startBody = await startResponse.Content.ReadAsStringAsync(cancellationToken);
            Explain(startResponse.StatusCode, startBody, "démarrage");

            // Le dépôt existe (id connu) mais n'a pas démarré. On le renvoie en
            // « Unknown » AVEC sa référence : la réconciliation ira lire son vrai statut
            // (il peut rester « pending », auquel cas aucun argent n'est parti).
            return PayoutResult.Unknown(
                "Versement transmis à l'opérateur, confirmation en attente.",
                payoutId.ToString());
        }

        // « Accepted » = créé + démarré. Le versement est EN COURS, pas confirmé.
        return PayoutResult.Accepted(payoutId.Value.ToString());
    }

    /// <summary>
    /// Statut réel d'un dépôt (GET /payouts/{id}). C'est la seule source de vérité :
    /// « sent » prouve le versement, « failed » autorise le remboursement.
    /// </summary>
    public async Task<PayoutStatusResult> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(FedaPayHttpGateway.ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"payouts/{providerReference}");
            Authorize(request);

            var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Ce message ne remonte PAS au vendeur : sur « Unknown », la réconciliation
                // ne touche pas au retrait. Il n'a de valeur que pour nous — donc au journal.
                _logger.LogWarning(
                    "FedaPay payout — lecture du statut impossible pour {Reference} ({StatusCode}). Réponse : {Body}",
                    providerReference, (int)response.StatusCode, body);

                return new PayoutStatusResult(PayoutStatus.Unknown,
                    $"FedaPay — lecture du statut impossible ({(int)response.StatusCode}).");
            }

            return new PayoutStatusResult(MapStatus(ExtractStatus(body)), null);
        }
        catch (Exception ex)
        {
            // Panne de lecture : surtout ne rien conclure — on réessaiera.
            _logger.LogWarning(ex, "FedaPay payout — statut indéterminé pour {Reference}.", providerReference);
            return new PayoutStatusResult(PayoutStatus.Unknown, "FedaPay — statut indéterminé.");
        }
    }

    /// <summary>
    /// Webhook de dépôt FedaPay. Payload : { "name": "payout.sent", "entity": { id, status } }.
    ///
    /// Deux gardes essentielles :
    ///  1. On n'accepte que les événements dont le NOM commence par « payout. ». Les
    ///     événements de transaction partagent la même URL et les identifiants FedaPay
    ///     sont propres à chaque type d'entité (le dépôt n°4212 et la transaction n°4212
    ///     existent tous les deux) : sans ce filtre, un « payout.canceled » irait chercher
    ///     un PAIEMENT portant le même numéro et le marquerait échoué.
    ///  2. Signature vérifiée AVANT toute interprétation : un webhook falsifié pourrait
    ///     sinon faire clôturer un retrait jamais versé, ou déclencher un remboursement.
    /// </summary>
    public PayoutWebhookEvent ParseWebhook(string rawBody, string? signatureHeader)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
            var root = document.RootElement;

            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            if (!name.StartsWith("payout", StringComparison.OrdinalIgnoreCase))
            {
                return PayoutWebhookEvent.NotPayout;
            }

            // À partir d'ici l'événement NOUS concerne : une signature invalide est un rejet,
            // pas un « on ignore » (on ne veut pas acquitter un faux webhook en silence).
            if (!FedaPayHttpGateway.VerifyFedaPaySignature(rawBody, signatureHeader, _options.WebhookSecret))
            {
                return PayoutWebhookEvent.Unsigned;
            }

            var entity = root.TryGetProperty("entity", out var e) ? e : root;
            var reference = entity.TryGetProperty("id", out var id)
                ? (id.ValueKind == JsonValueKind.Number ? id.GetInt64().ToString() : id.GetString())
                : null;

            // Le statut de l'entité fait foi ; à défaut, on le déduit du nom de
            // l'événement (« payout.sent » → sent), car c'est parfois la seule info.
            var status = entity.TryGetProperty("status", out var s) ? s.GetString() : null;
            status ??= name.Contains('.') ? name[(name.IndexOf('.') + 1)..] : null;

            return new PayoutWebhookEvent(IsPayoutEvent: true, Verified: true, reference, MapStatus(status));
        }
        catch (JsonException)
        {
            // Payload illisible : on ne peut pas affirmer qu'il s'agit d'un dépôt.
            return PayoutWebhookEvent.NotPayout;
        }
    }

    /// <summary>4xx (hors 408/429) = le PSP a tranché : rien n'est parti.</summary>
    private static bool IsDefinitiveRejection(HttpStatusCode status)
        => (int)status is >= 400 and < 500
           && status is not HttpStatusCode.RequestTimeout and not HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Opérateur du vendeur → (mode FedaPay, pays). Le mode encode le pays : on ne
    /// peut donc PAS router un opérateur inconnu, ni un opérateur multi-pays ambigu
    /// (« Wave » existe en CI et au SN — sans pays sur le compte, on refuse).
    /// Ajouter un pays au compte de versement du vendeur permettra d'étendre cette table.
    /// </summary>
    private static (string Mode, string Country)? ResolveRoute(string? provider) =>
        (provider ?? string.Empty).ToLowerInvariant() switch
        {
            "mtnmomo" or "mtn" or "mtn_open" => ("mtn_open", "bj"), // MTN Bénin
            "moovmoney" or "moov" => ("moov", "bj"),                // Moov Bénin
            "celtis" or "sbin" => ("sbin", "bj"),                   // Celtis Bénin
            _ => null                                               // non routable → refus explicite
        };

    private void Authorize(HttpRequestMessage request)
        => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

    private static (string First, string Last) SplitName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return ("Vendeur", "Marketplace");
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts[0], parts[0]);
    }

    /// <summary>Numéro au format E.164 attendu par FedaPay (« +229XXXXXXXX »).</summary>
    private static string ToE164(string? msisdn, string country)
    {
        var digits = new string((msisdn ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return string.Empty;
        }

        var callingCode = country switch
        {
            "bj" => "229", // Bénin
            "ci" => "225", // Côte d'Ivoire
            "tg" => "228", // Togo
            "sn" => "221", // Sénégal
            "ne" => "227", // Niger
            "bf" => "226", // Burkina Faso
            "ml" => "223", // Mali
            "gn" => "224", // Guinée
            _ => "229"
        };

        return digits.StartsWith(callingCode) ? "+" + digits : "+" + callingCode + digits;
    }

    /// <summary>Email stable dérivé du numéro (clé d'unicité client chez FedaPay).</summary>
    private static string BuildEmail(string e164)
    {
        var digits = new string(e164.Where(char.IsDigit).ToArray());
        return $"versement-{digits}@payouts.hba-marketplace.fr";
    }

    private static long? ExtractPayoutId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var payout = root.TryGetProperty("v1/payout", out var wrapped) ? wrapped : root;
        if (payout.TryGetProperty("id", out var id))
        {
            return id.ValueKind == JsonValueKind.Number ? id.GetInt64() : long.TryParse(id.GetString(), out var n) ? n : null;
        }

        return null;
    }

    private static string? ExtractStatus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var payout = root.TryGetProperty("v1/payout", out var wrapped) ? wrapped : root;
        return payout.TryGetProperty("status", out var status) ? status.GetString() : null;
    }

    /// <summary>Statuts FedaPay documentés : pending, started, processing, sent, failed.</summary>
    private static PayoutStatus MapStatus(string? status) => (status ?? string.Empty).ToLowerInvariant() switch
    {
        "pending" => PayoutStatus.Pending,
        "started" => PayoutStatus.Started,
        "processing" => PayoutStatus.Processing,
        "sent" => PayoutStatus.Sent,
        "failed" => PayoutStatus.Failed,
        _ => PayoutStatus.Unknown
    };
}
