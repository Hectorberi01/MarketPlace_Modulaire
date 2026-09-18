using System.Text;
using HbaExpress.Dispatch.Application.Abstractions;

namespace HbaExpress.Dispatch.Api.Outbound;

/// <summary>
/// Appelle la marketplace en HTTP signé.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA ROUTE APPELÉE ICI FAIT SORTIR DE L'ARGENT DU PORTEFEUILLE DE LA PLATEFORME.
///
/// Elle attend une signature HMAC du corps brut sous l'en-tête « X-Delivery-Signature »,
/// et la refuse en production si le secret manque. Le secret utilisé ici est donc le
/// MÊME que « Delivery:WebhookSecret » côté marketplace — et il est DISTINCT de celui du
/// sens entrant : une fuite d'un côté ne doit pas ouvrir les deux.
///
/// On signe la chaîne stockée en file, pas une re-sérialisation : voir IMarketplaceClient.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class MarketplaceClient : IMarketplaceClient
{
    public const string HttpClientName = "marketplace";

    /// <summary>
    /// Adresse de base de l'API marketplace.
    ///
    /// SANS CHEMIN, ET SANS SLASH FINAL SIGNIFICATIF. Les routes appelées commencent par
    /// « / » : combinées à une base qui porterait un préfixe (« http://hote/api »), elles
    /// l'ÉCRASERAIENT silencieusement et viseraient « http://hote/internal/... ». Le
    /// symptôme serait un 404 en production, pour une configuration qui paraît juste.
    /// </summary>
    public const string BaseUrlKey = "Marketplace:BaseUrl";

    /// <summary>Secret du sens DISPATCH → MARKETPLACE (« Delivery:WebhookSecret » là-bas).</summary>
    public const string SecretKey = "Marketplace:OutboundSecret";

    /// <summary>En-tête attendu par la marketplace (voir DeliveryEndpoints).</summary>
    private const string SignatureHeader = "X-Delivery-Signature";

    // L'horodatage voyage sous InboundSignature.TimestampHeader — même nom d'en-tête
    // dans les deux sens, parce que c'est la même convention, pas le même secret.

    private const string CourseCompletedPath = "/api/delivery/courses/completed";

    /// <summary>
    /// L'échec de course. MÊME SECRET, MÊME SIGNATURE, MÊME CONVENTION D'ADRESSE que la
    /// livraison : c'est le même canal, avec un fait différent à annoncer.
    /// </summary>
    private const string CourseFailedPath = "/api/delivery/courses/failed";

    /// <summary>
    /// Le réveil du coursier. MÊME SECRET, MÊME SIGNATURE, MÊME CONVENTION D'ADRESSE que
    /// les deux autres — un seul dispositif d'authentification entre les deux services.
    ///
    /// Ce qu'elle ne partage PAS avec elles : la file. Voir SendCourierOfferAsync.
    /// </summary>
    private const string CourierOfferPath = "/api/delivery/couriers/offer";

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MarketplaceClient> _logger;

    public MarketplaceClient(HttpClient http, IConfiguration configuration, ILogger<MarketplaceClient> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendCourierDeliveryCompletedAsync(
        string payloadJson, CancellationToken cancellationToken = default)
        => PostAsync(CourseCompletedPath, payloadJson, cancellationToken);

    public Task SendCourierDeliveryFailedAsync(
        string payloadJson, CancellationToken cancellationToken = default)
        => PostAsync(CourseFailedPath, payloadJson, cancellationToken);

    public Task SendCourierOfferAsync(
        string payloadJson, CancellationToken cancellationToken = default)
        => PostAsync(CourierOfferPath, payloadJson, cancellationToken);

    private async Task PostAsync(string path, string body, CancellationToken cancellationToken)
    {
        if (_http.BaseAddress is null)
        {
            // Adresse absente : on lève plutôt que d'inventer une destination. Le drain
            // réessaie, puis abandonne — et l'abandon se voit, là où un repli silencieux
            // sur « localhost » ne se verrait nulle part.
            throw new InvalidOperationException(
                $"ADRESSE DE LA MARKETPLACE ABSENTE ({BaseUrlKey}).\n\n" +
                "Aucune course livrée ne sera payée tant qu'elle manque. Renseignez " +
                "Marketplace:BaseUrl (variable Marketplace__BaseUrl) et Marketplace:OutboundSecret.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var secret = _configuration[SecretKey];
        if (!string.IsNullOrWhiteSpace(secret))
        {
            // Horodatage ET signature, posés ensemble. Signés à l'INSTANT DE L'ENVOI :
            // le drain réessaie un message pendant des minutes, et c'est la fraîcheur de
            // l'APPEL qu'on atteste, pas celle de la mise en file.
            var (timestamp, signature) = Security.InboundSignature.Sign(body, secret);
            request.Headers.Add(Security.InboundSignature.TimestampHeader, timestamp);
            request.Headers.Add(SignatureHeader, signature);
        }

        using var response = await _http.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Marketplace prévenue : {Path} — {Status}.", path, (int)response.StatusCode);
            return;
        }

        var detail = await ReadShortAsync(response, cancellationToken);

        throw new HttpRequestException(
            $"Marketplace : {path} a répondu {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}");
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
            // Confort de diagnostic : ne jamais laisser la lecture du corps masquer le
            // vrai échec, qui est le code de réponse.
            return string.Empty;
        }
    }
}
