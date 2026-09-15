using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// CRÉE AU DÉMARRAGE LES BUCKETS DÉCLARÉS QUI N'EXISTENT PAS ENCORE.
///
/// Séparer les médias par usage veut dire déclarer des buckets dans la configuration.
/// Sans ce service, ajouter un usage demanderait d'aller le créer à la main dans le
/// tableau de bord AVANT de déployer, sous peine d'un « NoSuchBucket » au premier
/// téléversement — c'est-à-dire devant un vendeur, et sans rien qui explique pourquoi.
///
/// CE QUE CE SERVICE NE FAIT PAS, ET NE PEUT PAS FAIRE. Un bucket créé par l'API S3
/// est PRIVÉ. Rattacher un domaine public — r2.dev ou domaine personnalisé — n'existe
/// pas dans l'API S3 : cela se fait une fois, à la main, dans le tableau de bord
/// Cloudflare. Ce service garantit donc que L'ÉCRITURE aboutit, jamais que la LECTURE
/// publique fonctionne. C'est exactement pourquoi `CloudflareR2MediaOptions` n'aiguille
/// un usage vers son bucket dédié que lorsque son domaine public est AUSSI renseigné :
/// un bucket créé mais pas encore exposé ne doit pas commencer à recevoir des images
/// dont l'URL ne répondra pas.
///
/// IL FAUT UN JETON ADMIN. Créer un bucket n'est pas une opération d'objet : un jeton
/// « Object Read & Write » reçoit un 403. C'est une raison sérieuse de laisser
/// `AutoCreateBuckets` à faux en production et de créer les buckets à la main —
/// le conteneur applicatif n'a alors aucun moyen de créer ni de détruire un bucket.
/// L'échec est de toute façon sans conséquence : on journalise et on continue.
///
/// NON BLOQUANT PAR CONSTRUCTION. Ce travail ne doit jamais empêcher l'API de
/// démarrer : R2 indisponible, jeton insuffisant, DNS en rade — l'API sert le
/// catalogue, les commandes et les paiements sans avoir besoin d'un bucket.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class R2BucketProvisioner : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CloudflareR2MediaOptions _options;
    private readonly ILogger<R2BucketProvisioner> _logger;

    public R2BucketProvisioner(
        IHttpClientFactory httpClientFactory,
        CloudflareR2MediaOptions options,
        ILogger<R2BucketProvisioner> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.AutoCreateBuckets || !_options.IsConfigured)
        {
            return;
        }

        foreach (var bucket in _options.BucketsUtilises())
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await GarantirAsync(bucket, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "R2 : impossible de vérifier ou créer le bucket « {Bucket} ». Les téléversements vers ce bucket échoueront tant qu'il n'existe pas.",
                    bucket);
            }
        }
    }

    private async Task GarantirAsync(string bucket, CancellationToken cancellationToken)
    {
        if (!EstUnNomDeBucketValide(bucket))
        {
            _logger.LogWarning(
                "R2 : « {Bucket} » n'est pas un nom de bucket valide (3 à 63 caractères, minuscules, chiffres et tirets). Aucune tentative de création.",
                bucket);
            return;
        }

        var client = _httpClientFactory.CreateClient(CloudflareR2MediaStorage.ClientName);

        using var sonde = SignerRequeteBucket(HttpMethod.Head, bucket);
        using var reponseSonde = await client.SendAsync(sonde, cancellationToken);

        if (reponseSonde.IsSuccessStatusCode)
        {
            return;
        }

        if (reponseSonde.StatusCode != HttpStatusCode.NotFound)
        {
            // 403 : jeton sans droit d'administration, ou bucket appartenant à un
            // autre compte. Dans les deux cas, créer n'aboutira pas davantage, et
            // insister ne ferait qu'ajouter une seconde ligne de journal.
            _logger.LogWarning(
                "R2 : le bucket « {Bucket} » n'a pas pu être vérifié ({Code}). Aucune création tentée.",
                bucket,
                (int)reponseSonde.StatusCode);
            return;
        }

        using var creation = SignerRequeteBucket(HttpMethod.Put, bucket);
        using var reponseCreation = await client.SendAsync(creation, cancellationToken);

        // 409 « BucketAlreadyOwnedByYou » : quelqu'un d'autre l'a créé entre la sonde
        // et ici (autre instance de l'API qui démarre en même temps). C'est le
        // résultat voulu, pas une erreur.
        if (reponseCreation.IsSuccessStatusCode || reponseCreation.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogInformation("R2 : bucket « {Bucket} » créé.", bucket);
            return;
        }

        var corps = await reponseCreation.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "R2 : échec de création du bucket « {Bucket} » ({Code}) : {Corps}. Un jeton « Admin Read & Write » est nécessaire pour créer un bucket.",
            bucket,
            (int)reponseCreation.StatusCode,
            corps);
    }

    /// <summary>
    /// Règles de nommage R2 : 3 à 63 caractères, minuscules, chiffres et tirets, ni
    /// commençant ni finissant par un tiret. On vérifie AVANT d'émettre la requête —
    /// un nom invalide construirait un chemin qui ne désigne pas ce qu'on croit.
    /// </summary>
    private static bool EstUnNomDeBucketValide(string bucket)
    {
        if (bucket.Length is < 3 or > 63)
        {
            return false;
        }

        if (bucket[0] == '-' || bucket[^1] == '-')
        {
            return false;
        }

        return bucket.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-');
    }

    /// <summary>
    /// Signe une requête de NIVEAU BUCKET (HEAD / PUT sur « /{bucket} »), charge utile
    /// vide et sans en-tête `content-type`.
    ///
    /// La signature est refaite ici plutôt que réutilisée depuis
    /// <see cref="CloudflareR2MediaStorage"/> : celle-là signe des requêtes d'OBJET,
    /// avec un content-type dans les en-têtes canoniques et une clé dans l'URI.
    /// Généraliser une seule méthode pour les deux formes reviendrait à multiplier les
    /// paramètres optionnels sur le code le plus délicat du fichier, pour économiser
    /// vingt lignes.
    /// </summary>
    private HttpRequestMessage SignerRequeteBucket(HttpMethod method, string bucket)
    {
        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var host = new Uri(_options.Endpoint).Authority;
        var payloadHash = Hex(SHA256.HashData(Array.Empty<byte>()));
        var canonicalUri = "/" + bucket;

        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate
        };

        var signedHeaders = string.Join(';', headers.Keys);
        var canonicalHeaders = string.Concat(headers.Select(h => $"{h.Key}:{h.Value}\n"));
        var canonicalRequest = $"{method.Method}\n{canonicalUri}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

        var scope = $"{dateStamp}/{_options.Region}/s3/aws4_request";
        var stringToSign =
            "AWS4-HMAC-SHA256\n" + amzDate + "\n" + scope + "\n" +
            Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));

        var signingKey = GetSignatureKey(_options.SecretAccessKey, dateStamp, _options.Region, "s3");
        var signature = Hex(HmacSha256(signingKey, stringToSign));
        var authorization =
            $"AWS4-HMAC-SHA256 Credential={_options.AccessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";

        var request = new HttpRequestMessage(method, $"{_options.Endpoint}/{bucket}");
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return request;
    }

    private static byte[] HmacSha256(byte[] key, string data)
        => new HMACSHA256(key).ComputeHash(Encoding.UTF8.GetBytes(data));

    private static byte[] GetSignatureKey(string secret, string dateStamp, string region, string service)
    {
        var kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + secret), dateStamp);
        var kRegion = HmacSha256(kDate, region);
        var kService = HmacSha256(kRegion, service);
        return HmacSha256(kService, "aws4_request");
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
