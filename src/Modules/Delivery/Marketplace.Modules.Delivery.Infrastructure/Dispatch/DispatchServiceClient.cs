using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marketplace.BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Marketplace.Modules.Delivery.Infrastructure.Dispatch;

/// <summary>
/// Appelle le service de dispatch en HTTP signé.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UN ÉCHEC LÈVE. C'EST TOUTE LA FIABILITÉ DE CE CANAL, ET ELLE EST EMPRUNTÉE.
///
/// Ce client n'a ni file, ni réessai, ni journal de ce qu'il a envoyé — et il n'en a pas
/// besoin : il est appelé DEPUIS L'OUTBOX. Le message est déjà persisté dans la même
/// transaction que la commande ; l'outbox réessaie avec délai croissant et met en lettre
/// morte au bout du plafond, avec sa métrique Prometheus. Lever une exception, c'est lui
/// rendre la main.
///
/// Ce qu'il ne faut SURTOUT pas faire ici, c'est avaler l'erreur pour « ne pas casser le
/// traitement ». Un catch silencieux transformerait une panne du service de dispatch en
/// commandes qui ne partent jamais, sans erreur, sans lettre morte, sans alerte — la
/// panne exacte qu'on vient de corriger côté monolithe.
///
/// TOUT CODE NON-2XX LÈVE, y compris un 400. Un 400 signifie que la marketplace a produit
/// une course que le dispatch refuse de construire — un ramassage sans commune, par
/// exemple. Le réessai n'y changera rien, et c'est justement pour cela qu'il doit finir en
/// LETTRE MORTE : une lettre morte se voit, un « return » silencieux ne se voit pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DispatchServiceClient : IDispatchServiceClient
{
    /// <summary>Nom du client HTTP nommé, enregistré par DeliveryModuleInstaller.</summary>
    public const string HttpClientName = "dispatch-service";

    /// <summary>
    /// Adresse de base du service. Sans elle, ce client refuse d'inventer une destination.
    ///
    /// SANS CHEMIN, ET SANS SLASH FINAL SIGNIFICATIF. Les routes appelées commencent par
    /// « / » : combinées à une base qui porterait un préfixe (« http://hote/api »), elles
    /// l'ÉCRASERAIENT silencieusement et viseraient « http://hote/internal/... ». Le
    /// symptôme serait un 404 en production, pour une configuration qui paraît juste.
    /// </summary>
    public const string BaseUrlKey = "Dispatch:BaseUrl";

    /// <summary>
    /// Secret du sens MARKETPLACE → DISPATCH. DISTINCT de « Delivery:WebhookSecret », qui
    /// protège le sens inverse : une fuite d'un côté ne doit pas ouvrir les deux.
    /// </summary>
    public const string SecretKey = "Dispatch:OutboundSecret";

    /// <summary>En-tête attendu par le service (voir InboundSignature côté dispatch).</summary>
    private const string SignatureHeader = "X-Marketplace-Signature";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DispatchServiceClient> _logger;

    public DispatchServiceClient(
        HttpClient http, IConfiguration configuration, ILogger<DispatchServiceClient> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendOrderReadyAsync(DispatchOrderReady payload, CancellationToken cancellationToken = default)
        => PostAsync("/internal/orders/ready-for-delivery", payload, $"commande {payload.OrderId}", cancellationToken);

    public Task SendOrderCancelledAsync(DispatchOrderCancelled payload, CancellationToken cancellationToken = default)
        => PostAsync("/internal/orders/cancelled", payload, $"commande {payload.OrderId}", cancellationToken);

    public Task SendCourierStandingAsync(DispatchCourierStanding payload, CancellationToken cancellationToken = default)
        => PostAsync("/internal/couriers/standing-changed", payload, $"coursier {payload.CourierId}", cancellationToken);

    /// <summary>
    /// Budget d'attente d'une LECTURE, distinct des dix secondes du client HTTP.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POURQUOI PAS LES DIX SECONDES DÉJÀ CONFIGURÉES.
    ///
    /// Ce délai-là est taillé pour le drain d'outbox, qui traite en série et peut se
    /// permettre d'attendre : personne ne regarde. Cette lecture-ci alimente une carte
    /// que l'écran redemande toutes les quinze secondes. Attendre dix secondes une
    /// réponse qui ne viendra pas, c'est immobiliser un fil de requête presque jusqu'au
    /// rafraîchissement suivant, et faire patienter l'administrateur devant une carte
    /// figée là où « service injoignable » serait la bonne réponse, et l'honnête.
    ///
    /// Quatre secondes suffisent : le service est dans le même réseau, et la requête
    /// est une projection de quelques dizaines de lignes en mémoire.
    /// ─────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(4);

    public async Task<DispatchFleet> GetCourierPositionsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        const string path = "/internal/couriers/positions";

        // Le budget de lecture s'ajoute à l'annulation de l'appelant, il ne la remplace
        // pas : un client qui ferme sa page doit toujours couper l'appel immédiatement.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ReadBudget);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        var secret = _configuration[SecretKey];
        if (!string.IsNullOrWhiteSpace(secret))
        {
            // CORPS VIDE, SIGNÉ QUAND MÊME. La chaîne signée est « horodatage.corps » :
            // avec un corps vide elle vaut « horodatage. », ce que le service recompose
            // à l'identique. La signature atteste l'identité de l'appelant, l'horodatage
            // borne la validité de sa demande à cinq minutes — il ne supprime pas le
            // rejeu, il l'enferme dans cette fenêtre, ce qui est sans conséquence sur une
            // LECTURE. Le point d'attention, noté côté dispatch, est que la chaîne signée
            // ne porte ni la méthode ni le chemin : ce couple d'en-têtes vaut pour tout
            // appel `/internal` à corps vide.
            var (timestamp, signature) = PartnerServiceSignature.Sign(string.Empty, secret);
            request.Headers.Add(PartnerServiceSignature.TimestampHeader, timestamp);
            request.Headers.Add(SignatureHeader, signature);
        }

        using var response = await _http.SendAsync(request, budget.Token);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadShortAsync(response, budget.Token);

            throw new HttpRequestException(
                $"Service de dispatch : {path} a répondu {(int)response.StatusCode} " +
                $"({response.ReasonPhrase}). {detail}");
        }

        var flotte = await response.Content.ReadFromJsonAsync<DispatchFleet>(JsonOptions, budget.Token);

        // Un corps « null » est un JSON valide et donnerait ici une référence nulle ; un
        // corps `{}` donnerait un `Couriers` nul. Le service n'émet ni l'un ni l'autre,
        // mais l'appelant n'a pas à s'en remettre à cette promesse — et une réponse
        // amputée doit se voir : elle passe par le même chemin qu'une réponse illisible.
        if (flotte is null || flotte.Couriers is null)
        {
            throw new JsonException(
                "Service de dispatch : réponse de la flotte vide ou amputée de ses coursiers.");
        }

        return flotte;
    }

    private async Task PostAsync<TPayload>(
        string path, TPayload payload, string subject, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        // ─────────────────────────────────────────────────────────────────────────
        // ON SÉRIALISE UNE FOIS, ON SIGNE CETTE CHAÎNE, ON ENVOIE CETTE CHAÎNE.
        //
        // PostAsJsonAsync sérialiserait de son côté — avec potentiellement un autre ordre
        // de champs ou un autre format de décimal — et la signature porterait alors sur
        // des octets différents de ceux transmis. Le service rejetterait un appel
        // pourtant légitime, et l'erreur ne se voit qu'en production.
        // ─────────────────────────────────────────────────────────────────────────
        var body = JsonSerializer.Serialize(payload, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var secret = _configuration[SecretKey];
        if (!string.IsNullOrWhiteSpace(secret))
        {
            // Horodatage ET signature, posés ensemble : le service rejette un appel qui
            // n'a que l'un des deux. On signe à l'INSTANT DE L'ENVOI, pas à celui de la
            // mise en file — un message qui a attendu dans l'outbox et part maintenant
            // est frais maintenant, et c'est la fraîcheur de l'APPEL qu'on atteste.
            var (timestamp, signature) = PartnerServiceSignature.Sign(body, secret);
            request.Headers.Add(PartnerServiceSignature.TimestampHeader, timestamp);
            request.Headers.Add(SignatureHeader, signature);
        }

        using var response = await _http.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "Service de dispatch prévenu : {Path} pour {Subject} — {Status}.",
                path, subject, (int)response.StatusCode);
            return;
        }

        // Le corps est lu pour le message d'erreur, tronqué : le service répond en
        // ProblemDetails, et seul son début est utile dans un journal.
        var detail = await ReadShortAsync(response, cancellationToken);

        throw new HttpRequestException(
            $"Service de dispatch : {path} pour {subject} a répondu {(int)response.StatusCode} " +
            $"({response.ReasonPhrase}). {detail}");
    }

    /// <summary>
    /// Adresse absente = on lève, plutôt que de retomber sur une valeur par défaut.
    ///
    /// Un repli silencieux sur « http://localhost:5210 » donnerait un système qui paraît
    /// marcher en développement et perd toutes les courses ailleurs. Levée ici, l'erreur
    /// remonte à l'outbox, qui la journalise puis met en lettre morte — la configuration
    /// manquante devient visible au lieu de rester une hypothèse.
    /// </summary>
    private void EnsureConfigured()
    {
        if (_http.BaseAddress is not null)
        {
            return;
        }

        throw new DispatchNotConfiguredException(
            $"ADRESSE DU SERVICE DE DISPATCH ABSENTE ({BaseUrlKey}).\n\n" +
            "Aucune commande prête ne sera transmise au service coursier tant qu'elle manque. " +
            "Renseignez Dispatch:BaseUrl (variable Dispatch__BaseUrl), par exemple " +
            "http://dispatch:8080, ainsi que Dispatch:OutboundSecret.");
    }

    private static async Task<string> ReadShortAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return content.Length <= 500 ? content : content[..500];
        }
        catch (Exception)
        {
            // Le corps est un confort de diagnostic : ne jamais laisser sa lecture
            // masquer le vrai échec, qui est le code de réponse.
            return string.Empty;
        }
    }
}

/// <summary>
/// L'adresse du service de dispatch n'est pas renseignée sur cette instance.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UN TYPE À PART, POUR QUE LE MESSAGE QU'ON AFFICHE SOIT VRAI.
///
/// C'était une `InvalidOperationException` nue. Or `ObjectDisposedException` en dérive,
/// comme plusieurs erreurs d'usage d'EF, de `HttpClient` et du conteneur. L'appelant qui
/// dégrade proprement — l'API publique du module — la traduisait alors en « renseignez
/// Dispatch__BaseUrl » : un message impératif, précis, et faux, qui envoie un
/// administrateur corriger une configuration parfaitement correcte.
///
/// Elle reste une `InvalidOperationException` : tout code qui l'attrapait continue de
/// l'attraper. Ce type ne fait que la rendre RECONNAISSABLE.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DispatchNotConfiguredException : InvalidOperationException
{
    public DispatchNotConfiguredException(string message) : base(message)
    {
    }
}
